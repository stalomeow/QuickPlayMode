# Quick Play Mode

When entering play mode, automatically reset static members of dirty types without [Domain Reloading](https://docs.unity3d.com/Manual/DomainReloading.html). Advantages:

- Fast execution with low additional overhead.
- Simple setup; just add a few attributes, and the rest is automatic.
- No impact on the code of release builds.

> Formerly known as EasyTypeReload.

## Requirements

- Unity >= 2021.3.
- Mono Cecil >= 1.10.1.

## Install via git URL

![install-git-url-1](/Screenshots~/install-git-url-1.png)

![install-git-url-2](/Screenshots~/install-git-url-2.png)

## Editor Extensions

![menu-item](/Screenshots~/menu_item.png)

You can manually reset static members of types or reload the domain.

**Remember to click `Enter Play Mode Options/Reload Domain` to disable Domain Reloading!**

## Examples

```csharp
// Use the namespace
using QuickPlayMode;
// using ...

// Mark the type
[ReloadOnEnterPlayMode]
public static class ExampleGeneric<T> // Support generics
{
    // Automatically reset to default(T)
    public static T Value;

    // Automatically reset to null
    public static event Action<T> Event;

    // Automatically reset to (new List<T>(114))
    public static List<T> Property { get; set; } = new List<T>(114);

    // This method is called before the type is reloaded
    // OrderInType defaults to 0, lower number executes earlier
    // * Multiple callbacks within a type are sorted, but not between types
    [RunBeforeReload(OrderInType = 100)]
    static void UnloadSecond()
    {
        Debug.Log("514");
    }

    // This method is also called before the type is reloaded
    // UnloadFirst() is called before UnloadSecond()
    [RunBeforeReload]
    static void UnloadFirst()
    {
        Debug.Log("114");
    }

    // Mark the type
    [ReloadOnEnterPlayMode]
    public static class ExampleNestedNonGeneric // Support nested types
    {
        // Automatically reset to (new()
        // {
        //     "Hello",
        //     "World"
        // })
        public static List<string> ListValue = new()
        {
            "Hello",
            "World"
        };

        // .cctor will be executed again
        static ExampleNestedNonGeneric()
        {
            Debug.Log("ExampleNestedNonGeneric..cctor()");
        }
    }

    // Mark the type
    [ReloadOnEnterPlayMode]
    public static class ExampleNestedGeneric<U> // Support generic types nested in other generic types
    {
        // Automatically reset to default(KeyValuePair<T, U>)
        public static KeyValuePair<T, U> KVPValue;
    }
}

// Not marked with [ReloadOnEnterPlayMode]
public static class ExampleIgnoredClass
{
    // Will not be automatically reset
    public static string Value;

    // Will not be executed again
    static ExampleIgnoredClass() { }

    // Will not be called
    [RunBeforeReload]
    static void Unload() { }
}
```

## Readonly Fields

In the Unity Editor, the `readonly` keyword is removed from all fields in marked types, as shown in the example below:

```csharp
[ReloadOnEnterPlayMode]
public class Program
{
    public static readonly string a = "aaa";
    public static readonly string b = a + "bbb";

    // In the Unity Editor, the plugin removes the readonly keyword from the fields
    // public static string a = "aaa";
    // public static string b = a + "bbb";
}
```

This process is automatic and generally does not require attention. If you want to preserve the `readonly` keyword, you need to add the `PreserveReadonly` parameter.

```csharp
[ReloadOnEnterPlayMode(PreserveReadonly = true)]
public class Program
{
    public static readonly string a = "aaa";
    public static readonly string b = a + "bbb";

    // Preserves the readonly keyword
    // public static readonly string a = "aaa";
    // public static readonly string b = a + "bbb";
}
```

However, preserving the `readonly` keyword may lead to issues.

In the provided code, `a` can be reset successfully. However, the value of `b` after reset is unpredictable because its value depends on `a`. This is likely an issue with Unity Mono. After resetting the value of `a`, at the low-level, `a` seems to briefly become a dangling pointer, causing the result of `a + "bbb"` to be unpredictable.

In most cases, there is no need to preserve the `readonly` keyword in the Unity Editor unless you require their metadata. If you must preserve it, it is advisable to perform thorough testing to check for potential errors.

## How Does It Work?

All of the following work is done automatically and only within the Unity Editor. When building for production, there will be no traces of this package, not even in the metadata, because of the [Managed code stripping](https://docs.unity3d.com/Manual/ManagedCodeStripping.html).

### 1. Hook Assembly

Insert the following code into the assembly. At runtime, it will record the assembly's types that have been used.

```csharp
using System;
using System.Runtime.CompilerServices;
using System.Threading;

[CompilerGenerated]
internal static class <AssemblyTypeReloader>
{
    private static Action s_UnloadActions;

    private static Action s_LoadActions;

    public static void RegisterUnload(Action value)
    {
        Action action = s_UnloadActions;
        Action action2;
        do
        {
            action2 = action;
            Action value2 = (Action)Delegate.Combine(action2, value);
            action = Interlocked.CompareExchange(ref s_UnloadActions, value2, action2);
        }
        while ((object)action != action2);
    }

    public static void Unload()
    {
        s_UnloadActions?.Invoke();
    }

    public static void RegisterLoad(Action value)
    {
        Action action = s_LoadActions;
        Action action2;
        do
        {
            action2 = action;
            Action value2 = (Action)Delegate.Combine(action2, value);
            action = Interlocked.CompareExchange(ref s_LoadActions, value2, action2);
        }
        while (action != action2);
    }

    public static void Load()
    {
        s_LoadActions?.Invoke();
    }
}
```

### 2. Hook Type

Using `ExampleGeneric<T>` from the previous Examples:

#### Copy Class Constructor (.cctor)

```csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__ClassConstructor__Copy()
{
    Property = new List<T>(114);
}
```

#### Generate code: Call RunBeforeReload callbacks in order

```csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__UnloadType__Impl()
{
    UnloadFirst();
    UnloadSecond();
}
```

#### Generate code: Reset all fields and re-execute .cctor

```csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__LoadType__Impl()
{
    Value = default(T);
    ExampleGeneric<T>.Event = null;
    Property = null;
    <ExampleGeneric`1>__ClassConstructor__Copy();
}
```

#### Insert code in the original .cctor

```csharp
static ExampleGeneric()
{
    Property = new List<T>(114);

    // Code is inserted below
    <AssemblyTypeReloader>.RegisterUnload(<ExampleGeneric`1>__UnloadType__Impl);
    <AssemblyTypeReloader>.RegisterLoad(<ExampleGeneric`1>__LoadType__Impl);
}
```

### 3. Listen for EnterPlayMode Event in Unity Editor

When entering Play Mode, reload dirty types.

```csharp
public static class TypeReloader
{
    private static bool s_Initialized = false;
    private static Action s_UnloadTypesAction;
    private static Action s_LoadTypesAction;

    public static void ReloadDirtyTypes()
    {
        try
        {
            InitializeIfNot();

            s_UnloadTypesAction?.Invoke();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            s_LoadTypesAction?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("Failed to reload dirty types!");
        }
    }

    [InitializeOnEnterPlayMode]
    private static void OnEnterPlayModeInEditor(EnterPlayModeOptions options)
    {
        if ((options & EnterPlayModeOptions.DisableDomainReload) == 0)
        {
            return;
        }

        ReloadDirtyTypes();
    }

    // ...
}
```
