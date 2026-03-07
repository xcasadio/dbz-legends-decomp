using System;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var game = new DbzLegendsAnalyser.Game1();
        game.Run();
    }
}
