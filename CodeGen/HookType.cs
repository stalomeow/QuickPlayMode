using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace EasyTypeReload.CodeGen
{
    internal static class HookType
    {
        public static int Execute(AssemblyDefinition assembly, MethodReference registerUnloadMethod, MethodReference registerLoadMethod, List<DiagnosticMessage> outDiagnostics)
        {
            ModuleDefinition mainModule = assembly.MainModule;
            Stack<TypeDefinition> stack = new(mainModule.Types);
            int hookCount = 0;

            while (stack.TryPop(out TypeDefinition type))
            {
                foreach (TypeDefinition nestedType in type.NestedTypes)
                {
                    stack.Push(nestedType);
                }

                if (!NeedReload(type, out List<FieldDefinition> staticFields, out List<MethodDefinition> unloadCallbacks, outDiagnostics))
                {
                    continue;
                }

                MethodDefinition cctor = GetClassConstructor(type);
                MethodDefinition copiedClassConstructor = CopyClassConstructorIfNeeded(type, cctor, mainModule);
                MethodDefinition unloadMethod = DefUnloadMethodIfNeeded(type, unloadCallbacks, mainModule);
                MethodDefinition loadMethod = DefLoadMethodIfNeeded(type, staticFields, copiedClassConstructor, mainModule);
                HookClassConstructor(type, ref cctor, unloadMethod, loadMethod, registerUnloadMethod, registerLoadMethod, mainModule);
                hookCount++;
            }

            return hookCount;
        }

        private static bool NeedReload(
            TypeDefinition type,
            out List<FieldDefinition> staticFields,
            out List<MethodDefinition> unloadCallbacks,
            List<DiagnosticMessage> outDiagnostics)
        {
            staticFields = new List<FieldDefinition>();
            unloadCallbacks = new List<MethodDefinition>();

            if (type.CustomAttributes.Get<ReloadOnEnterPlayModeAttribute>() == null)
            {
                return false;
            }

            FilterStaticFields(type, staticFields, outDiagnostics);
            FilterAndSortUnloadCallbacks(type, unloadCallbacks);

            return staticFields.Count > 0 || unloadCallbacks.Count > 0;
        }

        private static void FilterStaticFields(TypeDefinition type, List<FieldDefinition> outStaticFields, List<DiagnosticMessage> outDiagnostics)
        {
            foreach (FieldDefinition field in type.Fields)
            {
                if (!field.IsStatic || field.IsLiteral)
                {
                    continue;
                }

                outStaticFields.Add(field);
                AddFieldDiagnosticMessages(field, outDiagnostics);
            }
        }

        static void AddFieldDiagnosticMessages(FieldDefinition field, List<DiagnosticMessage> outDiagnostics)
        {
            if (field.CustomAttributes.Get<ForceReloadAttribute>() != null)
            {
                return;
            }

            if (field.IsInitOnly)
            {
                outDiagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = $"Field '{field.Name}' in {field.DeclaringType.FullName} is marked readonly, " +
                                  $"which may (e.g., when its value depends on another readonly field) cause undefined behavior after it is reloaded. " +
                                  $"Consider removing readonly, or using const instead. " +
                                  $"If you are sure it won't go wrong, you can add [field: {nameof(ForceReloadAttribute)}] to the relevant fields, properties, and events to suppress this message."
                });
            }
        }

        private static void FilterAndSortUnloadCallbacks(TypeDefinition type, List<MethodDefinition> outUnloadCallbacks)
        {
            Dictionary<MethodDefinition, int> callbackOrders = new();

            foreach (MethodDefinition method in type.Methods)
            {
                if (method.HasGenericParameters || method.HasParameters || method.ReturnType.FullName != typeof(void).FullName)
                {
                    continue;
                }

                CustomAttribute attr = method.CustomAttributes.Get<RunBeforeReloadAttribute>();

                if (attr == null)
                {
                    continue;
                }

                int order = 0;

                foreach (CustomAttributeNamedArgument prop in attr.Properties)
                {
                    if (prop.Name == nameof(RunBeforeReloadAttribute.OrderInType))
                    {
                        order = (int)prop.Argument.Value;
                        break;
                    }
                }

                callbackOrders[method] = order;
                outUnloadCallbacks.Add(method);
            }

            outUnloadCallbacks.Sort((x, y) => callbackOrders[x] - callbackOrders[y]);
        }

        private static void HookClassConstructor(
            TypeDefinition type,
            ref MethodDefinition cctor,
            MethodDefinition unloadMethodOrNull,
            MethodDefinition loadMethodOrNull,
            MethodReference registerUnloadMethod,
            MethodReference registerLoadMethod,
            ModuleDefinition mainModule)
        {
            if (unloadMethodOrNull == null && loadMethodOrNull == null)
            {
                return;
            }

            if (cctor == null)
            {
                const MethodAttributes methodAttributes = MethodAttributes.Private | MethodAttributes.ReuseSlot |
                                                          MethodAttributes.Static | MethodAttributes.HideBySig |
                                                          MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;

                cctor = new MethodDefinition(".cctor", methodAttributes, mainModule.TypeSystem.Void);
                type.Methods.Add(cctor);
            }

            MethodReference actionCtor = GetSystemActionConstructor(mainModule);
            ILProcessor il = cctor.Body.GetILProcessor();

            if (il.Body.Instructions.Count > 0)
            {
                il.Body.Instructions.RemoveAt(il.Body.Instructions.Count - 1); // Remove OpCodes.Ret
            }

            // Register Unload
            if (unloadMethodOrNull != null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, WrapMethodIfDeclaringTypeIsGeneric(unloadMethodOrNull));
                il.Emit(OpCodes.Newobj, actionCtor);
                il.Emit(OpCodes.Call, registerUnloadMethod);
            }

            // Register Load
            if (loadMethodOrNull != null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, WrapMethodIfDeclaringTypeIsGeneric(loadMethodOrNull));
                il.Emit(OpCodes.Newobj, actionCtor);
                il.Emit(OpCodes.Call, registerLoadMethod);
            }

            il.Emit(OpCodes.Ret);
        }

        private static MethodDefinition DefUnloadMethodIfNeeded(
            TypeDefinition type,
            List<MethodDefinition> unloadCallbacks,
            ModuleDefinition mainModule)
        {
            if (unloadCallbacks.Count == 0)
            {
                return null;
            }

            const MethodAttributes methodAttributes = MethodAttributes.Private | MethodAttributes.ReuseSlot |
                                                      MethodAttributes.Static | MethodAttributes.HideBySig;

            MethodDefinition method = new($"<{type.Name}>__UnloadType__Impl", methodAttributes, mainModule.TypeSystem.Void);
            AddCompilerGeneratedAttribute(method.CustomAttributes, mainModule);
            type.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();

            // Invoke Unload Callbacks
            foreach (MethodDefinition unloadCallback in unloadCallbacks)
            {
                il.Emit(OpCodes.Call, WrapMethodIfDeclaringTypeIsGeneric(unloadCallback));
            }

            il.Emit(OpCodes.Ret);

            return method;
        }

        private static MethodDefinition DefLoadMethodIfNeeded(
            TypeDefinition type,
            List<FieldDefinition> staticFields,
            MethodDefinition copiedClassConstructorOrNull,
            ModuleDefinition mainModule)
        {
            if (staticFields.Count == 0 && copiedClassConstructorOrNull == null)
            {
                return null;
            }

            const MethodAttributes methodAttributes = MethodAttributes.Private | MethodAttributes.ReuseSlot |
                                                      MethodAttributes.Static | MethodAttributes.HideBySig;

            MethodDefinition method = new($"<{type.Name}>__LoadType__Impl", methodAttributes, mainModule.TypeSystem.Void);
            AddCompilerGeneratedAttribute(method.CustomAttributes, mainModule);
            type.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();

            // Reset Fields to Zero
            foreach (FieldDefinition field in staticFields)
            {
                il.Emit(OpCodes.Ldsflda, WrapFieldIfDeclaringTypeIsGeneric(field));
                il.Emit(OpCodes.Initobj, field.FieldType);
            }

            // Init Type
            if (copiedClassConstructorOrNull != null)
            {
                il.Emit(OpCodes.Call, WrapMethodIfDeclaringTypeIsGeneric(copiedClassConstructorOrNull));
            }

            il.Emit(OpCodes.Ret);

            return method;
        }

        private static MethodDefinition CopyClassConstructorIfNeeded(
            TypeDefinition type,
            MethodDefinition cctor,
            ModuleDefinition mainModule)
        {
            if (cctor == null)
            {
                return null;
            }

            const MethodAttributes methodAttributes = MethodAttributes.Private | MethodAttributes.ReuseSlot |
                                                      MethodAttributes.Static | MethodAttributes.HideBySig;

            MethodDefinition method = new($"<{type.Name}>__ClassConstructor__Copy", methodAttributes, mainModule.TypeSystem.Void);
            AddCompilerGeneratedAttribute(method.CustomAttributes, mainModule);
            type.Methods.Add(method);

            // Copy method
            method.Body.InitLocals = cctor.Body.InitLocals;
            foreach (Instruction ins in cctor.Body.Instructions)
            {
                method.Body.Instructions.Add(ins);
            }
            foreach (VariableDefinition varDef in cctor.Body.Variables)
            {
                method.Body.Variables.Add(varDef);
            }
            foreach (ExceptionHandler exHandler in cctor.Body.ExceptionHandlers)
            {
                method.Body.ExceptionHandlers.Add(exHandler);
            }

            return method;
        }

        private static MethodDefinition GetClassConstructor(TypeDefinition type)
        {
            return type.Methods.FirstOrDefault(method => method.IsConstructor && method.Name == ".cctor");
        }

        private static TypeReference GetSystemActionType(ModuleDefinition mainModule)
        {
            return new TypeReference("System", "Action", mainModule, mainModule.TypeSystem.CoreLibrary, false);
        }

        private static MethodReference GetSystemActionConstructor(ModuleDefinition mainModule)
        {
            TypeReference actionType = GetSystemActionType(mainModule);
            return new MethodReference(".ctor", mainModule.TypeSystem.Void, actionType)
            {
                HasThis = true,
                Parameters =
                {
                    new ParameterDefinition(mainModule.TypeSystem.Object),
                    new ParameterDefinition(mainModule.TypeSystem.IntPtr),
                }
            };
        }

        private static FieldReference WrapFieldIfDeclaringTypeIsGeneric(FieldReference field)
        {
            if (field.DeclaringType.HasGenericParameters)
            {
                var declaringType = new GenericInstanceType(field.DeclaringType);
                foreach (var parameter in field.DeclaringType.GenericParameters)
                {
                    declaringType.GenericArguments.Add(parameter);
                }

                field = new FieldReference(field.Name, field.FieldType, declaringType);
            }

            return field;
        }

        private static MethodReference WrapMethodIfDeclaringTypeIsGeneric(MethodReference method)
        {
            if (method.DeclaringType.HasGenericParameters)
            {
                var declaringType = new GenericInstanceType(method.DeclaringType);
                foreach (var parameter in method.DeclaringType.GenericParameters)
                {
                    declaringType.GenericArguments.Add(parameter);
                }

                method = new MethodReference(method.Name, method.ReturnType, declaringType)
                {
                    CallingConvention = method.CallingConvention,
                    ExplicitThis = method.ExplicitThis,
                    HasThis = method.HasThis
                };
            }

            return method;
        }

        private static void AddCompilerGeneratedAttribute(Collection<CustomAttribute> attributes, ModuleDefinition mainModule)
        {
            var type = new TypeReference("System.Runtime.CompilerServices", "CompilerGeneratedAttribute",
                mainModule, mainModule.TypeSystem.CoreLibrary, false);
            var ctor = new MethodReference(".ctor", mainModule.TypeSystem.Void, type) { HasThis = true };
            attributes.Add(new CustomAttribute(ctor));
        }

        private static CustomAttribute Get<T>(this Collection<CustomAttribute> attributes) where T : Attribute
        {
            return attributes.FirstOrDefault(attr => attr.AttributeType.FullName == typeof(T).FullName);
        }
    }
}
