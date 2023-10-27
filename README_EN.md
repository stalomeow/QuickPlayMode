# EasyTypeReload

[![openupm](https://img.shields.io/npm/v/com.stalomeow.easy-type-reload?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.cn/packages/com.stalomeow.easy-type-reload/)

An easy-to-use package for Unity that automatically resets static members of types when you enter play mode without [reloading domain](https://docs.unity3d.com/Manual/DomainReloading.html). It has low runtime overhead and can be entirely stripped from build.

[中文版](/README.md)

## Requirements

- Unity >= 2021.3.
- Mono Cecil >= 1.10.1.

## Examples

After importing the package, simply add attributes to your types. No additional configuration is needed.

```csharp
// Use the namespace
using EasyTypeReload;
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

## Singleton

It’s almost the same as writing without using this package.

```csharp
using EasyTypeReload;
// using ...

[ReloadOnEnterPlayMode]
public abstract class BaseManager<T> where T : BaseManager<T>, new()
{
    public static T Instance { get; } = new T();

    [RunBeforeReload]
    private static void UnloadInstance() => Instance.Dispose();

    protected BaseManager() { }

    protected virtual void Dispose() { }
}

public class CountManager : BaseManager<CountManager>
{
    private int m_Count = 0;

    public CountManager()
    {
        Debug.Log($"Create {nameof(CountManager)}");
    }

    public void IncreaseCount()
    {
        m_Count++;
    }

    public void PrintCount()
    {
        print(m_Count);
    }
}
```

## Editor Extensions

It provides a menu item. You can manually reload previously used types or manually reload the domain.

![menu-item](/Screenshots~/menu_item.png)

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
