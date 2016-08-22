using System.Collections.Generic;
using System.Diagnostics;
using dnSpy.Contracts.Decompiler;

namespace ICSharpCode.Decompiler.ILAst {
	static class Utils
	{
		public static void NopMergeBinSpans(ILBlockBase block, List<ILNode> newBody, int instrIndexToRemove)
		{
			var body = block.Body;
			ILNode prevNode = null, nextNode = null;
			ILExpression prev = null, next = null;
			if (newBody.Count > 0)
				prev = (prevNode = newBody[newBody.Count - 1]) as ILExpression;
			if (instrIndexToRemove + 1 < body.Count)
				next = (nextNode = body[instrIndexToRemove + 1]) as ILExpression;

			ILNode node = null;

			if (prev != null && prev.Prefixes == null) {
				switch (prev.Code) {
				case ILCode.Call:
				case ILCode.CallGetter:
				case ILCode.Calli:
				case ILCode.CallSetter:
				case ILCode.Callvirt:
				case ILCode.CallvirtGetter:
				case ILCode.CallvirtSetter:
					node = prev;
					break;
				}
			}

			if (next != null && next.Prefixes == null) {
				if (next.Match(ILCode.Leave))
					node = next;
			}

			if (node != null && node == prevNode)
				AddBinSpansTryPreviousFirst(body[instrIndexToRemove], prevNode, nextNode, block);
			else
				AddBinSpansTryNextFirst(body[instrIndexToRemove], prevNode, nextNode, block);
		}

		public static void LabelMergeBinSpans(ILBlockBase block, List<ILNode> newBody, int instrIndexToRemove)
		{
			var body = block.Body;
			ILNode prevNode = null, nextNode = null;
			if (newBody.Count > 0)
				prevNode = newBody[newBody.Count - 1];
			if (instrIndexToRemove + 1 < body.Count)
				nextNode = body[instrIndexToRemove + 1];

			AddBinSpansTryNextFirst(body[instrIndexToRemove], prevNode, nextNode, block);
		}

		public static void AddBinSpansTryPreviousFirst(ILNode removed, ILNode prev, ILNode next, ILBlockBase block)
		{
			if (removed == null)
				return;
			AddBinSpansTryPreviousFirst(prev, next, block, removed);
		}

		public static void AddBinSpansTryNextFirst(ILNode removed, ILNode prev, ILNode next, ILBlockBase block)
		{
			if (removed == null)
				return;
			AddBinSpansTryNextFirst(prev, next, block, removed);
		}

		public static void AddBinSpansTryPreviousFirst(ILNode prev, ILNode next, ILBlockBase block, ILNode removed)
		{
			if (prev != null && prev.SafeToAddToEndBinSpans)
				removed.AddSelfAndChildrenRecursiveBinSpans(prev.EndBinSpans);
			else if (next != null)
				removed.AddSelfAndChildrenRecursiveBinSpans(next.BinSpans);
			else if (prev != null)
				removed.AddSelfAndChildrenRecursiveBinSpans(block.EndBinSpans);
			else
				removed.AddSelfAndChildrenRecursiveBinSpans(block.BinSpans);
		}

		public static void AddBinSpansTryNextFirst(ILNode prev, ILNode next, ILBlockBase block, ILNode removed)
		{
			if (next != null)
				removed.AddSelfAndChildrenRecursiveBinSpans(next.BinSpans);
			else if (prev != null) {
				if (prev.SafeToAddToEndBinSpans)
					removed.AddSelfAndChildrenRecursiveBinSpans(prev.EndBinSpans);
				else
					removed.AddSelfAndChildrenRecursiveBinSpans(block.EndBinSpans);
			}
			else
				removed.AddSelfAndChildrenRecursiveBinSpans(block.BinSpans);
		}

		public static void AddBinSpansTryNextFirst(ILNode prev, ILNode next, ILBlockBase block, IEnumerable<BinSpan> binSpans)
		{
			if (next != null)
				next.BinSpans.AddRange(binSpans);
			else if (prev != null) {
				if (prev.SafeToAddToEndBinSpans)
					prev.EndBinSpans.AddRange(binSpans);
				else
					block.EndBinSpans.AddRange(binSpans);
			}
			else
				block.BinSpans.AddRange(binSpans);
		}

		public static void AddBinSpansTryPreviousFirst(List<ILNode> newBody, List<ILNode> body, int removedIndex, ILBlockBase block)
		{
			ILNode prev = newBody.Count > 0 ? newBody[newBody.Count - 1] : null;
			ILNode next = removedIndex + 1 < body.Count ? body[removedIndex + 1] : null;
			AddBinSpansTryPreviousFirst(body[removedIndex], prev, next, block);
		}

		public static void AddBinSpansTryNextFirst(List<ILNode> newBody, List<ILNode> body, int removedIndex, ILBlockBase block)
		{
			ILNode prev = newBody.Count > 0 ? newBody[newBody.Count - 1] : null;
			ILNode next = removedIndex + 1 < body.Count ? body[removedIndex + 1] : null;
			AddBinSpansTryNextFirst(body[removedIndex], prev, next, block);
		}

		/// <summary>
		/// Adds the removed instruction's BinSpans to the next or previous instruction
		/// </summary>
		/// <param name="block">The owner block</param>
		/// <param name="body">Body</param>
		/// <param name="removedIndex">Index of removed instruction</param>
		public static void AddBinSpans(ILBlockBase block, List<ILNode> body, int removedIndex)
		{
			AddBinSpans(block, body, removedIndex, 1);
		}

		/// <summary>
		/// Adds the removed instruction's BinSpans to the next or previous instruction
		/// </summary>
		/// <param name="block">The owner block</param>
		/// <param name="body">Body</param>
		/// <param name="removedIndex">Index of removed instruction</param>
		/// <param name="numRemoved">Number of removed instructions</param>
		public static void AddBinSpans(ILBlockBase block, List<ILNode> body, int removedIndex, int numRemoved)
		{
			var prev = removedIndex - 1 >= 0 ? body[removedIndex - 1] : null;
			var next = removedIndex + numRemoved < body.Count ? body[removedIndex + numRemoved] : null;

			ILNode node = null;
			if (node == null && next is ILExpression)
				node = next;
			if (node == null && prev is ILExpression)
				node = prev;
			if (node == null && next is ILLabel)
				node = next;
			if (node == null && prev is ILLabel)
				node = prev;
			if (node == null)
				node = next ?? prev;	// Using next before prev should work better

			for (int i = 0; i < numRemoved; i++)
				AddBinSpansToInstruction(node, prev, next, block, body[removedIndex + i]);
		}

		public static void AddBinSpans(ILBlockBase block, List<ILNode> body, int removedIndex, IEnumerable<BinSpan> binSpans)
		{
			var prev = removedIndex - 1 >= 0 ? body[removedIndex - 1] : null;
			var next = removedIndex + 1 < body.Count ? body[removedIndex + 1] : null;

			ILNode node = null;
			if (node == null && next is ILExpression)
				node = next;
			if (node == null && prev is ILExpression)
				node = prev;
			if (node == null && next is ILLabel)
				node = next;
			if (node == null && prev is ILLabel)
				node = prev;
			if (node == null)
				node = next ?? prev;	// Using next before prev should work better

			AddBinSpansToInstruction(node, prev, next, block, binSpans);
		}

		public static void AddBinSpansToInstruction(ILNode nodeToAddTo, ILNode prev, ILNode next, ILBlockBase block, ILNode removed)
		{
			Debug.Assert(nodeToAddTo == prev || nodeToAddTo == next || nodeToAddTo == block);
			if (nodeToAddTo != null) {
				if (nodeToAddTo == prev && prev.SafeToAddToEndBinSpans) {
					removed.AddSelfAndChildrenRecursiveBinSpans(prev.EndBinSpans);
					return;
				}
				else if (nodeToAddTo != null && nodeToAddTo == next) {
					removed.AddSelfAndChildrenRecursiveBinSpans(next.BinSpans);
					return;
				}
			}
			AddBinSpansTryNextFirst(prev, next, block, removed);
		}

		public static void AddBinSpansToInstruction(ILNode nodeToAddTo, ILNode prev, ILNode next, ILBlockBase block, IEnumerable<BinSpan> binSpans)
		{
			Debug.Assert(nodeToAddTo == prev || nodeToAddTo == next || nodeToAddTo == block);
			if (nodeToAddTo != null) {
				if (nodeToAddTo == prev && prev.SafeToAddToEndBinSpans) {
					prev.EndBinSpans.AddRange(binSpans);
					return;
				}
				else if (nodeToAddTo != null && nodeToAddTo == next) {
					next.BinSpans.AddRange(binSpans);
					return;
				}
			}
			AddBinSpansTryNextFirst(prev, next, block, binSpans);
		}
	}
}
