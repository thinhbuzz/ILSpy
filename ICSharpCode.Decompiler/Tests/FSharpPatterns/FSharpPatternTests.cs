using NUnit.Framework;

namespace ICSharpCode.Decompiler.Tests.FSharpPatterns {
	[TestFixture]
	public class FSharpPatternTests
	{
		[Test]
		public void FSharpUsingDecompilesToCSharpUsing_Debug()
		{
			var ilCode = TestHelpers.FuzzyReadResource("FSharpUsing.fs.Debug.il");
			var csharpCode = TestHelpers.FuzzyReadResource("FSharpUsing.fs.Debug.cs");
			TestHelpers.RunIL(ilCode, csharpCode);
		}

		[Test]
		public void FSharpUsingDecompilesToCSharpUsing_Release()
		{
			var ilCode = TestHelpers.FuzzyReadResource("FSharpUsing.fs.Release.il");
			var csharpCode = TestHelpers.FuzzyReadResource("FSharpUsing.fs.Release.cs");
			TestHelpers.RunIL(ilCode, csharpCode);
		}
	}
}
