using Mono.Cecil;
using Mono.Cecil.Cil;
using static EasyTypeReload.AssemblyTypeReloaderConsts;

namespace EasyTypeReload.CodeGen
{
    internal static class HookAssembly
    {
        public static void Execute(
            AssemblyDefinition assembly,
            out MethodDefinition registerUnloadMethod,
            out MethodDefinition registerLoadMethod)
        {
            ModuleDefinition mainModule = assembly.MainModule;
            TypeDefinition reloaderType = DefReloaderType(mainModule);

            // Unload
            FieldDefinition unloadActionsField = DefActionCallbackBackingField(reloaderType, UnloadActionsFieldName, mainModule);
            registerUnloadMethod = DefRegisterActionCallbackMethod(reloaderType, RegisterUnloadMethodName, unloadActionsField, mainModule);
            DefInvokeActionCallbackMethod(reloaderType, UnloadMethodName, unloadActionsField, mainModule);

            // Load
            FieldDefinition loadActionsField = DefActionCallbackBackingField(reloaderType, LoadActionsFieldName, mainModule);
            registerLoadMethod = DefRegisterActionCallbackMethod(reloaderType, RegisterLoadMethodName, loadActionsField, mainModule);
            DefInvokeActionCallbackMethod(reloaderType, LoadMethodName, loadActionsField, mainModule);
        }

        private static TypeDefinition DefReloaderType(ModuleDefinition mainModule)
        {
            const TypeAttributes typeAttributes = TypeAttributes.Class | TypeAttributes.NotPublic |
                                                  TypeAttributes.Abstract | TypeAttributes.Sealed |
                                                  TypeAttributes.AutoLayout | TypeAttributes.AnsiClass |
                                                  TypeAttributes.BeforeFieldInit;

            TypeDefinition type = new("", TypeName, typeAttributes, mainModule.TypeSystem.Object);
            mainModule.Types.Add(type);

            return type;
        }

        private static FieldDefinition DefActionCallbackBackingField(
            TypeDefinition reloaderType,
            string fieldName,
            ModuleDefinition mainModule)
        {
            const FieldAttributes fieldAttributes = FieldAttributes.Private | FieldAttributes.Static;

            TypeReference actionType = GetSystemActionType(mainModule);
            FieldDefinition field = new(fieldName, fieldAttributes, actionType);
            reloaderType.Fields.Add(field);
            return field;
        }

        private static MethodDefinition DefRegisterActionCallbackMethod(
            TypeDefinition reloaderType,
            string methodName,
            FieldDefinition callbackBackingField,
            ModuleDefinition mainModule)
        {
            const MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.ReuseSlot |
                                                      MethodAttributes.Static | MethodAttributes.HideBySig;

            TypeReference actionType = GetSystemActionType(mainModule);
            MethodReference delegateCombineMethod = GetDelegateCombineMethod(mainModule);
            MethodReference interlockedCompareExchangeMethod = GetInterlockedCompareExchangeMethod(mainModule, actionType);
            MethodDefinition method = new(methodName, methodAttributes, mainModule.TypeSystem.Void);
            reloaderType.Methods.Add(method);

            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, actionType));

            method.Body.Variables.Add(new VariableDefinition(actionType)); // action
            method.Body.Variables.Add(new VariableDefinition(actionType)); // action2
            method.Body.Variables.Add(new VariableDefinition(actionType)); // value2

            ILProcessor il = method.Body.GetILProcessor();

            // Action action = Field;
            il.Emit(OpCodes.Ldsfld, callbackBackingField);
            il.Emit(OpCodes.Stloc_0);

            // loop start
            int loopHeadInsIndex = method.Body.Instructions.Count;
            {
                // action2 = action;
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Stloc_1);

                // Action value2 = (Action)Delegate.Combine(action2, value);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, delegateCombineMethod);
                il.Emit(OpCodes.Castclass, actionType);
                il.Emit(OpCodes.Stloc_2);

                // action = Interlocked.CompareExchange(ref Field, value2, action2);
                il.Emit(OpCodes.Ldsflda, callbackBackingField);
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Call, interlockedCompareExchangeMethod);
                il.Emit(OpCodes.Stloc_0);

                // while ((object)action != action2);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Bne_Un_S, method.Body.Instructions[loopHeadInsIndex]);
            }
            // end loop

            il.Emit(OpCodes.Ret);

            return method;
        }

        private static MethodDefinition DefInvokeActionCallbackMethod(
            TypeDefinition reloaderType,
            string methodName,
            FieldDefinition callbackBackingField,
            ModuleDefinition mainModule)
        {
            const MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.ReuseSlot |
                                                      MethodAttributes.Static | MethodAttributes.HideBySig;

            MethodReference actionInvokeMethod = GetSystemActionInvokeMethod(mainModule);
            MethodDefinition method = new(methodName, methodAttributes, mainModule.TypeSystem.Void);
            reloaderType.Methods.Add(method);

            ILProcessor il = method.Body.GetILProcessor();
            Instruction invokeIns = il.Create(OpCodes.Callvirt, actionInvokeMethod);

            // Field?.Invoke();
            il.Emit(OpCodes.Ldsfld, callbackBackingField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, invokeIns);

            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            il.Append(invokeIns);
            il.Emit(OpCodes.Ret);

            return method;
        }

        private static TypeReference GetSystemActionType(ModuleDefinition mainModule)
        {
            return new TypeReference("System", "Action", mainModule, mainModule.TypeSystem.CoreLibrary, false);
        }

        private static MethodReference GetSystemActionInvokeMethod(ModuleDefinition mainModule)
        {
            TypeReference actionType = GetSystemActionType(mainModule);
            return new MethodReference("Invoke", mainModule.TypeSystem.Void, actionType)
            {
                HasThis = true,
            };
        }

        private static MethodReference GetDelegateCombineMethod(ModuleDefinition mainModule)
        {
            TypeReference delegateType = new("System", "Delegate", mainModule, mainModule.TypeSystem.CoreLibrary, false);
            return new MethodReference("Combine", delegateType, delegateType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(delegateType),
                    new ParameterDefinition(delegateType),
                }
            };
        }

        private static MethodReference GetInterlockedCompareExchangeMethod(ModuleDefinition mainModule, TypeReference genericArg)
        {
            TypeReference interlockedType = new("System.Threading", "Interlocked", mainModule, mainModule.TypeSystem.CoreLibrary, false);
            MethodReference compareExchangeMethod = new("CompareExchange", mainModule.TypeSystem.Void, interlockedType);

            GenericParameter t = new("T", compareExchangeMethod);
            compareExchangeMethod.GenericParameters.Add(t);
            compareExchangeMethod.HasThis = false;

            compareExchangeMethod.ReturnType = t;
            compareExchangeMethod.Parameters.Add(new ParameterDefinition(new ByReferenceType(t)));
            compareExchangeMethod.Parameters.Add(new ParameterDefinition(t));
            compareExchangeMethod.Parameters.Add(new ParameterDefinition(t));

            GenericInstanceMethod instanceMethod = new(compareExchangeMethod);
            instanceMethod.GenericArguments.Add(genericArg);
            return instanceMethod;
        }
    }
}
