using System;
using System.Runtime.CompilerServices;

namespace ICSharpCode.Decompiler {
	static class StackOverflow {
		internal static void Prevent() {
			// For an unknown to me reason, in .NET 8, the RuntimeHelpers.EnsureSufficientExecutionStack() method does not work.
#if NETCOREAPP
			if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
				throw new InsufficientExecutionStackException();
#else
			RuntimeHelpers.EnsureSufficientExecutionStack();
#endif
		}
	}
}
