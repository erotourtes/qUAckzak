namespace qUAckzak.Mod.Modes
{
    internal interface IModMode
    {
        string Name { get; }

        void Update();

        void Reset();
    }
}
