using MelonLoader;

[assembly: MelonInfo(typeof(NoopMod.Noop), "NoopMod", "0.0.1", "dsh")]
[assembly: MelonGame(null, null)]

namespace NoopMod;

/// <summary>完全空的 mod：没有 OnInitializeMelon、没有 OnUpdate、不引用游戏程序集。</summary>
public class Noop : MelonMod
{
}
