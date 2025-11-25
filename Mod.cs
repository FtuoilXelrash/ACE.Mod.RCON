namespace RCON;

public class Mod : BasicMod
{
    public Mod() : base() => Setup(nameof(RCON), new PatchClass(this));
}
