// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;
using ICSharpCode.Decompiler.Disassembler;

namespace ICSharpCode.Decompiler.ILAst {
	public abstract class ILNode : IEnumerable<ILNode>
	{
		public readonly List<BinSpan> BinSpans = new List<BinSpan>(1);

		public virtual List<BinSpan> EndBinSpans {
			get { return BinSpans; }
		}
		public virtual BinSpan GetAllBinSpans(ref long index, ref bool done) {
			if (index < BinSpans.Count)
				return BinSpans[(int)index++];
			done = true;
			return default(BinSpan);
		}

		public bool HasEndBinSpans {
			get { return BinSpans != EndBinSpans; }
		}

		public bool WritesNewLine {
			get { return !(this is ILLabel || this is ILExpression || this is ILSwitch.CaseBlock); }
		}

		public virtual bool SafeToAddToEndBinSpans {
			get { return false; }
		}

		public IEnumerable<BinSpan> GetSelfAndChildrenRecursiveBinSpans()
		{
			foreach (var node in GetSelfAndChildrenRecursive<ILNode>()) {
				long index = 0;
				bool done = false;
				for (;;) {
					var b = node.GetAllBinSpans(ref index, ref done);
					if (done)
						break;
					yield return b;
				}
			}
		}

		public void AddSelfAndChildrenRecursiveBinSpans(List<BinSpan> coll)
		{
			foreach (var a in GetSelfAndChildrenRecursive<ILNode>()) {
				long index = 0;
				bool done = false;
				for (;;) {
					var b = a.GetAllBinSpans(ref index, ref done);
					if (done)
						break;
					coll.Add(b);
				}
			}
		}

		public List<BinSpan> GetSelfAndChildrenRecursiveBinSpans_OrderAndJoin() {
			// The current callers save the list as an annotation so always create a new list here
			// instead of having them pass in a cached list.
			var list = new List<BinSpan>();
			AddSelfAndChildrenRecursiveBinSpans(list);
			return BinSpan.OrderAndCompactList(list);
		}

		public List<T> GetSelfAndChildrenRecursive<T>(Func<T, bool> predicate = null) where T: ILNode
		{
			List<T> result = new List<T>(16);
			AccumulateSelfAndChildrenRecursive(result, predicate);
			return result;
		}

		public List<T> GetSelfAndChildrenRecursive<T>(List<T> result, Func<T, bool> predicate = null) where T: ILNode
		{
			result.Clear();
			AccumulateSelfAndChildrenRecursive(result, predicate);
			return result;
		}
		
		void AccumulateSelfAndChildrenRecursive<T>(List<T> list, Func<T, bool> predicate) where T:ILNode
		{
			// Note: RemoveEndFinally depends on self coming before children
			T thisAsT = this as T;
			if (thisAsT != null && (predicate == null || predicate(thisAsT)))
				list.Add(thisAsT);
			int index = 0;
			for (;;) {
				var node = GetNext(ref index);
				if (node == null)
					break;
				node.AccumulateSelfAndChildrenRecursive(list, predicate);
			}
		}

		internal virtual ILNode GetNext(ref int index)
		{
			return null;
		}
		
		public ILNode GetChildren()
		{
			return this;
		}

		public ILNode_Enumerator GetEnumerator()
		{
			return new ILNode_Enumerator(this);
		}

		IEnumerator<ILNode> IEnumerable<ILNode>.GetEnumerator()
		{
			return new ILNode_Enumerator(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new ILNode_Enumerator(this);
		}

		public struct ILNode_Enumerator : IEnumerator<ILNode>
		{
			readonly ILNode node;
			int index;
			ILNode current;

			internal ILNode_Enumerator(ILNode node)
			{
				this.node = node;
				this.index = 0;
				this.current = null;
			}

			public ILNode Current
			{
				get { return current; }
			}

			object IEnumerator.Current
			{
				get { return current; }
			}

			public void Dispose()
			{
			}

			public bool MoveNext()
			{
				return (this.current = this.node.GetNext(ref index)) != null;
			}

			public void Reset()
			{
				this.index = 0;
			}
		}

		public override string ToString()
		{
			var output = new StringBuilderDecompilerOutput();
			WriteTo(output, null);
			return output.ToString().Replace("\r\n", "; ");
		}
		
		public abstract void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder);

		protected void UpdateDebugInfo(MethodDebugInfoBuilder builder, int startLoc, int endLoc, IEnumerable<BinSpan> ranges)
		{
			if (builder == null)
				return;
			foreach (var binSpan in BinSpan.OrderAndCompact(ranges))
				builder.Add(new SourceStatement(binSpan, new TextSpan(startLoc, endLoc - startLoc)));
		}

		protected void WriteHiddenStart(IDecompilerOutput output, MethodDebugInfoBuilder builder, IEnumerable<BinSpan> extraBinSpans = null)
		{
			var location = output.NextPosition;
			output.Write("{", BoxedTextColor.Punctuation);
			var ilr = new List<BinSpan>(BinSpans);
			if (extraBinSpans != null)
				ilr.AddRange(extraBinSpans);
			UpdateDebugInfo(builder, location, output.NextPosition, ilr);
			output.WriteLine();
			output.IncreaseIndent();
		}

		protected void WriteHiddenEnd(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			output.DecreaseIndent();
			var location = output.NextPosition;
			output.Write("}", BoxedTextColor.Punctuation);
			UpdateDebugInfo(builder, location, output.NextPosition, EndBinSpans);
			output.WriteLine();
		}
	}
	
	public abstract class ILBlockBase: ILNode
	{
		public List<ILNode> Body;
		public List<BinSpan> endBinSpans = new List<BinSpan>(1);

		public override List<BinSpan> EndBinSpans {
			get { return endBinSpans; }
		}
		public override BinSpan GetAllBinSpans(ref long index, ref bool done) {
			if (index < BinSpans.Count)
				return BinSpans[(int)index++];
			int i = (int)index - BinSpans.Count;
			if (i < endBinSpans.Count) {
				index++;
				return endBinSpans[i];
			}
			done = true;
			return default(BinSpan);
		}

		public override bool SafeToAddToEndBinSpans {
			get { return true; }
		}

		public ILBlockBase()
		{
			this.Body = new List<ILNode>();
		}

		public ILBlockBase(params ILNode[] body)
		{
			this.Body = new List<ILNode>(body);
		}

		public ILBlockBase(List<ILNode> body)
		{
			this.Body = body;
		}

		internal override ILNode GetNext(ref int index)
		{
			if (index < this.Body.Count)
				return this.Body[index++];
			return null;
		}
		
		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			WriteTo(output, builder, null);
		}

		internal void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder, IEnumerable<BinSpan> binSpans)
		{
			WriteHiddenStart(output, builder, binSpans);
			foreach(ILNode child in this.GetChildren()) {
				child.WriteTo(output, builder);
				if (!child.WritesNewLine)
					output.WriteLine();
			}
			WriteHiddenEnd(output, builder);
		}
	}
	
	public class ILBlock: ILBlockBase
	{
		public ILExpression EntryGoto;
		
		public ILBlock(params ILNode[] body) : base(body)
		{
		}
		
		public ILBlock(List<ILNode> body) : base(body)
		{
		}
		
		internal override ILNode GetNext(ref int index)
		{
			if (index == 0) {
				index = 1;
				if (this.EntryGoto != null)
					return this.EntryGoto;
			}
			if (index <= this.Body.Count)
				return this.Body[index++ - 1];

			return null;
		}
	}
	
	public class ILBasicBlock: ILBlockBase
	{
		// Body has to start with a label and end with unconditional control flow
	}
	
	public class ILLabel: ILNode
	{
		public string Name;

		public override bool SafeToAddToEndBinSpans {
			get { return true; }
		}

		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var location = output.NextPosition;
			output.Write(Name, this, DecompilerReferenceFlags.Local | DecompilerReferenceFlags.Definition, BoxedTextColor.Label);
			output.Write(":", BoxedTextColor.Punctuation);
			UpdateDebugInfo(builder, location, output.NextPosition, BinSpans);
		}
	}
	
	public class ILTryCatchBlock: ILNode
	{
		public class CatchBlock: ILBlock
		{
			public bool IsFilter;
			public TypeSig ExceptionType;
			public ILVariable ExceptionVariable;
			public List<BinSpan> StlocBinSpans = new List<BinSpan>(1);

			public override BinSpan GetAllBinSpans(ref long index, ref bool done) {
				if (index < BinSpans.Count)
					return BinSpans[(int)index++];
				int i = (int)index - BinSpans.Count;
				if (i < StlocBinSpans.Count) {
					index++;
					return StlocBinSpans[i];
				}
				done = true;
				return default(BinSpan);
			}

			public CatchBlock()
			{
			}

			public CatchBlock(bool calculateBinSpans, List<ILNode> body)
			{
				this.Body = body;
				if (calculateBinSpans && body.Count > 0 && body[0].Match(ILCode.Pop))
					body[0].AddSelfAndChildrenRecursiveBinSpans(StlocBinSpans);
			}
			
			public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
			{
				var startLoc = output.NextPosition;
				if (IsFilter) {
					output.Write("filter", BoxedTextColor.Keyword);
					output.Write(" ", BoxedTextColor.Text);
					output.Write(ExceptionVariable.Name, ExceptionVariable, DecompilerReferenceFlags.None, BoxedTextColor.Local);
				}
				else if (ExceptionType != null) {
					output.Write("catch", BoxedTextColor.Keyword);
					output.Write(" ", BoxedTextColor.Text);
					output.Write(ExceptionType.FullName, ExceptionType, DecompilerReferenceFlags.None, TextColorHelper.GetColor(ExceptionType));
					if (ExceptionVariable != null) {
						output.Write(" ", BoxedTextColor.Text);
						output.Write(ExceptionVariable.Name, ExceptionVariable, DecompilerReferenceFlags.None, BoxedTextColor.Local);
					}
				}
				else {
					output.Write("handler", BoxedTextColor.Keyword);
					output.Write(" ", BoxedTextColor.Text);
					output.Write(ExceptionVariable.Name, ExceptionVariable, DecompilerReferenceFlags.None, BoxedTextColor.Local);
				}
				UpdateDebugInfo(builder, startLoc, output.NextPosition, StlocBinSpans);
				output.Write(" ", BoxedTextColor.Text);
				base.WriteTo(output, builder);
			}
		}
		public class FilterILBlock: CatchBlock
		{
			public FilterILBlock()
			{
				IsFilter = true;
			}

			public CatchBlock HandlerBlock;
			
			public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
			{
				base.WriteTo(output, builder);
				HandlerBlock.WriteTo(output, builder);
			}
		}
		
		public ILBlock          TryBlock;
		public List<CatchBlock> CatchBlocks;
		public ILBlock          FinallyBlock;
		public ILBlock          FaultBlock;
		public FilterILBlock    FilterBlock;
		
		internal override ILNode GetNext(ref int index)
		{
			if (index == 0) {
				index = 1;
				if (this.TryBlock != null)
					return this.TryBlock;
			}
			if (index <= this.CatchBlocks.Count)
				return this.CatchBlocks[index++ - 1];
			if (index == this.CatchBlocks.Count + 1) {
				index++;
				if (this.FaultBlock != null)
					return this.FaultBlock;
			}
			if (index == this.CatchBlocks.Count + 2) {
				index++;
				if (this.FinallyBlock != null)
					return this.FinallyBlock;
			}
			if (index == this.CatchBlocks.Count + 3) {
				index++;
				if (this.FilterBlock != null)
					return this.FilterBlock;
			}
			if (index == this.CatchBlocks.Count + 4) {
				index++;
				if (this.FilterBlock != null && this.FilterBlock.HandlerBlock != null)
					return this.FilterBlock.HandlerBlock;
			}
			return null;
		}

		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			output.Write(".try", BoxedTextColor.Keyword);
			output.Write(" ", BoxedTextColor.Text);
			TryBlock.WriteTo(output, builder, BinSpans);
			foreach (CatchBlock block in CatchBlocks) {
				block.WriteTo(output, builder);
			}
			if (FaultBlock != null) {
				output.Write("fault", BoxedTextColor.Keyword);
				output.Write(" ", BoxedTextColor.Text);
				FaultBlock.WriteTo(output, builder);
			}
			if (FinallyBlock != null) {
				output.Write("finally", BoxedTextColor.Keyword);
				output.Write(" ", BoxedTextColor.Text);
				FinallyBlock.WriteTo(output, builder);
			}
			if (FilterBlock != null) {
				output.Write("filter", BoxedTextColor.Keyword);
				output.Write(" ", BoxedTextColor.Text);
				FilterBlock.WriteTo(output, builder);
			}
		}
	}
	
	public class ILVariable
	{
		public string Name;
		public bool GeneratedByDecompiler;
		public bool GeneratedByDecompilerButCanBeRenamed;
		public TypeSig Type;
		public Local OriginalVariable;
		public Parameter OriginalParameter;
		public object Id {
			get {
				if (id == null)
					Interlocked.CompareExchange(ref id, new object(), null);
				return id;
			}
		}
		object id;

		public bool IsPinned {
			get { return OriginalVariable != null && OriginalVariable.Type is PinnedSig; }
		}
		
		public bool IsParameter {
			get { return OriginalParameter != null; }
		}
		
		public override string ToString()
		{
			return Name;
		}
	}
	
	public class ILExpressionPrefix
	{
		public readonly ILCode Code;
		public readonly object Operand;
		
		public ILExpressionPrefix(ILCode code, object operand = null)
		{
			this.Code = code;
			this.Operand = operand;
		}
	}
	
	public class ILExpression : ILNode
	{
		public ILCode Code { get; set; }
		public object Operand { get; set; }
		public List<ILExpression> Arguments { get; set; }
		public ILExpressionPrefix[] Prefixes { get; set; }
		
		public TypeSig ExpectedType { get; set; }
		public TypeSig InferredType { get; set; }

		public override bool SafeToAddToEndBinSpans {
			get { return true; }
		}
		
		public static readonly object AnyOperand = new object();
		
		public ILExpression(ILCode code, object operand, List<ILExpression> args)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>(args);
		}

		public ILExpression(ILCode code, object operand)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>();
		}

		public ILExpression(ILCode code, object operand, ILExpression arg1)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>() { arg1 };
		}

		public ILExpression(ILCode code, object operand, ILExpression arg1, ILExpression arg2)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>() { arg1, arg2 };
		}

		public ILExpression(ILCode code, object operand, ILExpression arg1, ILExpression arg2, ILExpression arg3)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>() { arg1, arg2, arg3 };
		}

		public ILExpression(ILCode code, object operand, ILExpression[] args)
		{
			if (operand is ILExpression)
				throw new ArgumentException("operand");
			
			this.Code = code;
			this.Operand = operand;
			this.Arguments = new List<ILExpression>(args);
		}
		
		public void AddPrefix(ILExpressionPrefix prefix)
		{
			ILExpressionPrefix[] arr = this.Prefixes;
			if (arr == null)
				arr = new ILExpressionPrefix[1];
			else
				Array.Resize(ref arr, arr.Length + 1);
			arr[arr.Length - 1] = prefix;
			this.Prefixes = arr;
		}
		
		public ILExpressionPrefix GetPrefix(ILCode code)
		{
			var prefixes = this.Prefixes;
			if (prefixes != null) {
				foreach (ILExpressionPrefix p in prefixes) {
					if (p.Code == code)
						return p;
				}
			}
			return null;
		}
		
		internal override ILNode GetNext(ref int index)
		{
			if (index < Arguments.Count)
				return Arguments[index++];
			return null;
		}
		
		public bool IsBranch()
		{
			return this.Operand is ILLabel || this.Operand is ILLabel[];
		}
		
		public IEnumerable<ILLabel> GetBranchTargets()
		{
			if (this.Operand is ILLabel) {
				return new ILLabel[] { (ILLabel)this.Operand };
			} else if (this.Operand is ILLabel[]) {
				return (ILLabel[])this.Operand;
			} else {
				return new ILLabel[] { };
			}
		}
		
		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var startLoc = output.NextPosition;
			if (Operand is ILVariable && ((ILVariable)Operand).GeneratedByDecompiler) {
				if (Code == ILCode.Stloc && this.InferredType == null) {
					output.Write(((ILVariable)Operand).Name, Operand, DecompilerReferenceFlags.None, ((ILVariable)Operand).IsParameter ? BoxedTextColor.Parameter : BoxedTextColor.Local);
					output.Write(" ", BoxedTextColor.Text);
					output.Write("=", BoxedTextColor.Operator);
					output.Write(" ", BoxedTextColor.Text);
					Arguments.First().WriteTo(output, null);
					UpdateDebugInfo(builder, startLoc, output.NextPosition, this.GetSelfAndChildrenRecursiveBinSpans());
					return;
				} else if (Code == ILCode.Ldloc) {
					output.Write(((ILVariable)Operand).Name, Operand, DecompilerReferenceFlags.None, ((ILVariable)Operand).IsParameter ? BoxedTextColor.Parameter : BoxedTextColor.Local);
					if (this.InferredType != null) {
						output.Write(":", BoxedTextColor.Punctuation);
						this.InferredType.WriteTo(output, ILNameSyntax.ShortTypeName);
						if (this.ExpectedType != null && this.ExpectedType.FullName != this.InferredType.FullName) {
							output.Write("[", BoxedTextColor.Punctuation);
							output.Write("exp", BoxedTextColor.Keyword);
							output.Write(":", BoxedTextColor.Punctuation);
							this.ExpectedType.WriteTo(output, ILNameSyntax.ShortTypeName);
							output.Write("]", BoxedTextColor.Punctuation);
						}
					}
					UpdateDebugInfo(builder, startLoc, output.NextPosition, this.GetSelfAndChildrenRecursiveBinSpans());
					return;
				}
			}
			
			if (this.Prefixes != null) {
				foreach (var prefix in this.Prefixes) {
					output.Write(prefix.Code.GetName() + ".", BoxedTextColor.OpCode);
					output.Write(" ", BoxedTextColor.Text);
				}
			}
			
			output.Write(Code.GetName(), BoxedTextColor.OpCode);
			if (this.InferredType != null) {
				output.Write(":", BoxedTextColor.Punctuation);
				this.InferredType.WriteTo(output, ILNameSyntax.ShortTypeName);
				if (this.ExpectedType != null && this.ExpectedType.FullName != this.InferredType.FullName) {
					output.Write("[", BoxedTextColor.Punctuation);
					output.Write("exp", BoxedTextColor.Keyword);
					output.Write(":", BoxedTextColor.Punctuation);
					this.ExpectedType.WriteTo(output, ILNameSyntax.ShortTypeName);
					output.Write("]", BoxedTextColor.Punctuation);
				}
			} else if (this.ExpectedType != null) {
				output.Write("[", BoxedTextColor.Punctuation);
				output.Write("exp", BoxedTextColor.Keyword);
				output.Write(":", BoxedTextColor.Punctuation);
				this.ExpectedType.WriteTo(output, ILNameSyntax.ShortTypeName);
				output.Write("]", BoxedTextColor.Punctuation);
			}
			output.Write("(", BoxedTextColor.Punctuation);
			bool first = true;
			if (Operand != null) {
				if (Operand is ILLabel) {
					output.Write(((ILLabel)Operand).Name, Operand, DecompilerReferenceFlags.None, BoxedTextColor.Label);
				} else if (Operand is ILLabel[]) {
					ILLabel[] labels = (ILLabel[])Operand;
					for (int i = 0; i < labels.Length; i++) {
						if (i > 0) {
							output.Write(",", BoxedTextColor.Punctuation);
							output.Write(" ", BoxedTextColor.Text);
						}
						output.Write(labels[i].Name, labels[i], DecompilerReferenceFlags.None, BoxedTextColor.Label);
					}
				} else if (Operand is IMethod && (Operand as IMethod).MethodSig != null) {
					IMethod method = (IMethod)Operand;
					if (method.DeclaringType != null) {
						method.DeclaringType.WriteTo(output, ILNameSyntax.ShortTypeName);
						output.Write("::", BoxedTextColor.Operator);
					}
					output.Write(method.Name, method, DecompilerReferenceFlags.None, TextColorHelper.GetColor(method));
				} else if (Operand is IField) {
					IField field = (IField)Operand;
					field.DeclaringType.WriteTo(output, ILNameSyntax.ShortTypeName);
					output.Write("::", BoxedTextColor.Operator);
					output.Write(field.Name, field, DecompilerReferenceFlags.None, TextColorHelper.GetColor(field));
				} else if (Operand is ILVariable) {
					var ilvar = (ILVariable)Operand;
					output.Write(ilvar.Name, Operand, DecompilerReferenceFlags.None, ilvar.IsParameter ? BoxedTextColor.Parameter : BoxedTextColor.Local);
				} else {
					DisassemblerHelpers.WriteOperand(output, Operand);
				}
				first = false;
			}
			foreach (ILExpression arg in this.Arguments) {
				if (!first) {
					output.Write(",", BoxedTextColor.Punctuation);
					output.Write(" ", BoxedTextColor.Text);
				}
				arg.WriteTo(output, null);
				first = false;
			}
			output.Write(")", BoxedTextColor.Punctuation);
			UpdateDebugInfo(builder, startLoc, output.NextPosition, this.GetSelfAndChildrenRecursiveBinSpans());
		}
	}
	
	public class ILWhileLoop : ILNode
	{
		public ILExpression Condition;
		public ILBlock      BodyBlock;
		
		internal override ILNode GetNext(ref int index)
		{
			if (index == 0) {
				index = 1;
				if (this.Condition != null)
					return this.Condition;
			}
			if (index == 1) {
				index = 2;
				if (this.BodyBlock != null)
					return this.BodyBlock;
			}
			return null;
		}

		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var startLoc = output.NextPosition;
			output.Write("loop", BoxedTextColor.Keyword);
			output.Write(" ", BoxedTextColor.Text);
			output.Write("(", BoxedTextColor.Punctuation);
			if (this.Condition != null)
				this.Condition.WriteTo(output, null);
			output.Write(")", BoxedTextColor.Punctuation);
			var binSpans = new List<BinSpan>(BinSpans);
			if (this.Condition != null)
				this.Condition.AddSelfAndChildrenRecursiveBinSpans(binSpans);
			UpdateDebugInfo(builder, startLoc, output.NextPosition, binSpans);
			output.Write(" ", BoxedTextColor.Text);
			this.BodyBlock.WriteTo(output, builder);
		}
	}
	
	public class ILCondition : ILNode
	{
		public ILExpression Condition;
		public ILBlock TrueBlock;   // Branch was taken
		public ILBlock FalseBlock;  // Fall-though
		
		internal override ILNode GetNext(ref int index)
		{
			if (index == 0) {
				index = 1;
				if (this.Condition != null)
					return this.Condition;
			}
			if (index == 1) {
				index = 2;
				if (this.TrueBlock != null)
					return this.TrueBlock;
			}
			if (index == 2) {
				index = 3;
				if (this.FalseBlock != null)
					return this.FalseBlock;
			}
			return null;
		}

		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var startLoc = output.NextPosition;
			output.Write("if", BoxedTextColor.Keyword);
			output.Write(" ", BoxedTextColor.Text);
			output.Write("(", BoxedTextColor.Punctuation);
			Condition.WriteTo(output, null);
			output.Write(")", BoxedTextColor.Punctuation);
			var binSpans = new List<BinSpan>(BinSpans);
			Condition.AddSelfAndChildrenRecursiveBinSpans(binSpans);
			UpdateDebugInfo(builder, startLoc, output.NextPosition, binSpans);
			output.Write(" ", BoxedTextColor.Text);
			TrueBlock.WriteTo(output, builder);
			if (FalseBlock != null) {
				output.Write("else", BoxedTextColor.Keyword);
				output.Write(" ", BoxedTextColor.Text);
				FalseBlock.WriteTo(output, builder);
			}
		}
	}
	
	public class ILSwitch: ILNode
	{
		public class CaseBlock: ILBlock
		{
			public List<int> Values;  // null for the default case
			
			public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
			{
				if (this.Values != null) {
					foreach (int i in this.Values) {
						output.Write("case", BoxedTextColor.Keyword);
						output.Write(" ", BoxedTextColor.Text);
						output.Write(string.Format("{0}", i), BoxedTextColor.Number);
						output.WriteLine(":", BoxedTextColor.Punctuation);
					}
				} else {
					output.Write("default", BoxedTextColor.Keyword);
					output.WriteLine(":", BoxedTextColor.Punctuation);
				}
				output.IncreaseIndent();
				base.WriteTo(output, builder);
				output.DecreaseIndent();
			}
		}
		
		public ILExpression Condition;
		public List<CaseBlock> CaseBlocks = new List<CaseBlock>();
		public List<BinSpan> endBinSpans = new List<BinSpan>(1);

		public override List<BinSpan> EndBinSpans {
			get { return endBinSpans; }
		}
		public override BinSpan GetAllBinSpans(ref long index, ref bool done) {
			if (index < BinSpans.Count)
				return BinSpans[(int)index++];
			int i = (int)index - BinSpans.Count;
			if (i < endBinSpans.Count) {
				index++;
				return endBinSpans[i];
			}
			done = true;
			return default(BinSpan);
		}

		public override bool SafeToAddToEndBinSpans {
			get { return true; }
		}
		
		internal override ILNode GetNext(ref int index)
		{
			if (index == 0) {
				index = 1;
				return this.Condition;
			}
			if (index <= this.CaseBlocks.Count)
				return this.CaseBlocks[index++ - 1];
			return null;
		}
		
		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var startLoc = output.NextPosition;
			output.Write("switch", BoxedTextColor.Keyword);
			output.Write(" ", BoxedTextColor.Text);
			output.Write("(", BoxedTextColor.Punctuation);
			Condition.WriteTo(output, null);
			output.Write(")", BoxedTextColor.Punctuation);
			var binSpans = new List<BinSpan>(BinSpans);
			Condition.AddSelfAndChildrenRecursiveBinSpans(binSpans);
			UpdateDebugInfo(builder, startLoc, output.NextPosition, binSpans);
			output.Write(" ", BoxedTextColor.Text);
			WriteHiddenStart(output, builder);
			foreach (CaseBlock caseBlock in this.CaseBlocks) {
				caseBlock.WriteTo(output, builder);
			}
			WriteHiddenEnd(output, builder);
		}
	}
	
	public class ILFixedStatement : ILNode
	{
		public List<ILExpression> Initializers = new List<ILExpression>();
		public ILBlock      BodyBlock;
		
		internal override ILNode GetNext(ref int index)
		{
			if (index < this.Initializers.Count)
				return this.Initializers[index++];
			if (index == this.Initializers.Count) {
				index++;
				if (this.BodyBlock != null)
					return this.BodyBlock;
			}
			return null;
		}

		public override void WriteTo(IDecompilerOutput output, MethodDebugInfoBuilder builder)
		{
			var startLoc = output.NextPosition;
			output.Write("fixed", BoxedTextColor.Keyword);
			output.Write(" ", BoxedTextColor.Text);
			output.Write("(", BoxedTextColor.Punctuation);
			for (int i = 0; i < this.Initializers.Count; i++) {
				if (i > 0) {
					output.Write(",", BoxedTextColor.Punctuation);
					output.Write(" ", BoxedTextColor.Text);
				}
				this.Initializers[i].WriteTo(output, null);
			}
			output.Write(")", BoxedTextColor.Punctuation);
			var binSpans = new List<BinSpan>(BinSpans);
			foreach (var i in Initializers)
				i.AddSelfAndChildrenRecursiveBinSpans(binSpans);
			UpdateDebugInfo(builder, startLoc, output.NextPosition, binSpans);
			output.Write(" ", BoxedTextColor.Text);
			this.BodyBlock.WriteTo(output, builder);
		}
	}
}