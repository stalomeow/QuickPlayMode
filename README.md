# Quick Play Mode

[>> English Version <<](/README_EN.md)

进入播放模式时，自动按需重置类型的静态成员，不使用 [Domain Reloading](https://docs.unity3d.com/Manual/DomainReloading.html)。优势：

- 速度快，额外开销低。
- 没有复杂配置，只需要加几个 `Attribute`，其他全自动。
- 不影响发布构建时的代码。

> 之前叫 EasyTypeReload。

## 要求

- Unity >= 2021.3.
- Mono Cecil >= 1.10.1.

## 从 git URL 安装

![install-git-url-1](/Screenshots~/install-git-url-1.png)

![install-git-url-2](/Screenshots~/install-git-url-2.png)

## 编辑器扩展

![menu-item](/Screenshots~/menu_item.png)

可以手动重置类型的静态成员，或者手动 Reload Domain。

**记得点 `Enter Play Mode Options/Reload Domain` 关闭 Domain Reloading！**

## 示例

``` csharp
// 使用命名空间
using QuickPlayMode;
// using ...

// 标记类型
[ReloadOnEnterPlayMode]
public static class ExampleGeneric<T> // 支持泛型
{
    // 会被自动重置为 default(T)
    public static T Value;

    // 会被自动重置为 null
    public static event Action<T> Event;

    // 会被自动重置为 new List<T>(114)
    public static List<T> Property { get; set; } = new List<T>(114);

    // 在类型被重置前，会调用这个方法
    // OrderInType 默认为 0，数字越小执行越早
    // * 一个类型中的多个回调会被排序，但类型与类型间不会排序
    [RunBeforeReload(OrderInType = 100)]
    static void UnloadSecond()
    {
        Debug.Log("514");
    }

    // 在类型被重置前，也会调用这个方法
    // UnloadFirst() 在 UnloadSecond() 前被调用
    [RunBeforeReload]
    static void UnloadFirst()
    {
        Debug.Log("114");
    }

    // 标记类型
    [ReloadOnEnterPlayMode]
    public static class ExampleNestedNonGeneric // 支持嵌套类型
    {
        // 会被自动重置为 new()
        // {
        //     "Hello",
        //     "World"
        // }
        public static List<string> ListValue = new()
        {
            "Hello",
            "World"
        };

        // .cctor 会被重新执行
        static ExampleNestedNonGeneric()
        {
            Debug.Log("ExampleNestedNonGeneric..cctor()");
        }
    }

    // 标记类型
    [ReloadOnEnterPlayMode]
    public static class ExampleNestedGeneric<U> // 支持泛型嵌套泛型
    {
        // 会被自动重置为 default(KeyValuePair<T, U>)
        public static KeyValuePair<T, U> KVPValue;
    }
}

// 没有标记 [ReloadOnEnterPlayMode]
public static class ExampleIgnoredClass
{
    // 不会被自动重置
    public static string Value;

    // 不会重新执行
    static ExampleIgnoredClass() { }

    // 不会被调用
    [RunBeforeReload]
    static void Unload() { }
}
```

## 只读字段

在 Unity Editor 里，会把被标记的类型中所有的字段的 `readonly` 关键字去掉。例如：

``` csharp
[ReloadOnEnterPlayMode]
public class Program
{
    public static readonly string a = "aaa";
    public static readonly string b = a + "bbb";

    // 在 Unity Editor 里，插件会把字段上的 readonly 去掉
    // public static string a = "aaa";
    // public static string b = a + "bbb";
}
```

这个过程是自动完成的，一般不需要在意。如果要保留 `readonly` 关键字，需要加上 `PreserveReadonly` 参数。

``` csharp
[ReloadOnEnterPlayMode(PreserveReadonly = true)]
public class Program
{
    public static readonly string a = "aaa";
    public static readonly string b = a + "bbb";

    // 保留 readonly 关键字
    // public static readonly string a = "aaa";
    // public static readonly string b = a + "bbb";
}
```

然而，保留 `readonly` 关键字可能会导致问题。

上面的代码中，`a` 可以正常被重置。`b` 被重置后的值是无法预测的，因为它的值依赖 `a`。这大概是 Unity Mono 的问题。在重置完 `a` 的值后，在底层，`a` 似乎会短暂地变成野指针，导致 `a + "bbb"` 的结果无法预测。

通常情况下，在 Unity Editor 里不需要保留 `readonly` 关键字，除非你要用这部分元数据。如果一定要保留它的话，请先做一下测试，看看会不会出错。

## 原理？

下面所有的工作都是自动完成的，且只在 Editor 里才执行。正式打包时，经过 [Managed code stripping](https://docs.unity3d.com/Manual/ManagedCodeStripping.html)，这个插件的痕迹会完全消失，连元数据都不会留下。

### 1. Hook Assembly

在程序集中插入下面的代码。运行时用来记录该程序集中被使用过的类型。

``` csharp
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
        while ((object)action != action2);
    }

    public static void Load()
    {
        s_LoadActions?.Invoke();
    }
}
```

### 2. Hook Type

以示例中的 `ExampleGeneric<T>` 为例。

#### 复制 Class Constructor（.cctor）

``` csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__ClassConstructor__Copy()
{
    Property = new List<T>(114);
}
```

#### 生成代码：按顺序调用 RunBeforeReload 回调

``` csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__UnloadType__Impl()
{
    UnloadFirst();
    UnloadSecond();
}
```

#### 生成代码：重置所有字段，重新执行 .cctor

``` csharp
[CompilerGenerated]
private static void <ExampleGeneric`1>__LoadType__Impl()
{
    Value = default(T);
    ExampleGeneric<T>.Event = null;
    Property = null;
    <ExampleGeneric`1>__ClassConstructor__Copy();
}
```

#### 在原来的 .cctor 中插入代码

``` csharp
static ExampleGeneric()
{
    Property = new List<T>(114);

    // 下面是被插入的代码
    <AssemblyTypeReloader>.RegisterUnload(<ExampleGeneric`1>__UnloadType__Impl);
    <AssemblyTypeReloader>.RegisterLoad(<ExampleGeneric`1>__LoadType__Impl);
}
```

### 3. 在 Unity Editor 中监听 EnterPlayMode 事件

进入 Play Mode 时，重置类型。

``` csharp
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
