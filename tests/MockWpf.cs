namespace System.Windows.Input
{
    public enum Key
    {
        None,
        T,
        System
    }
}

namespace WinLens.Native
{
    [System.Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }
}
