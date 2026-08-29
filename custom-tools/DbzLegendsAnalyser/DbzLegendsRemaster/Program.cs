if (args.Length == 2 && args[0] == "--validate-bandai")
{
	System.Environment.ExitCode = DbzLegendsRemaster.Validation.BandaiStrValidation.Run(args[1]);
}
else if (args.Length == 2 && args[0] == "--validate-dbz-op")
{
	System.Environment.ExitCode = DbzLegendsRemaster.Validation.BandaiStrValidation.RunDbzOp(args[1]);
}
else if (args.Length == 3 && args[0] == "--validate-xa-transition")
{
	System.Environment.ExitCode = DbzLegendsRemaster.Validation.BandaiStrValidation.RunXaTransition(
		args[1], args[2]);
}
else if (args.Length == 2 && args[0] == "--validate-str-v2")
{
	System.Environment.ExitCode = DbzLegendsRemaster.Validation.BandaiStrValidation.RunV2Smoke(args[1]);
}
else
{
	using var game = new DbzLegendsRemaster.Game1();
	game.Run();
}
