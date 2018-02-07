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
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace ICSharpCode.Decompiler.Disassembler {
	/// <summary>
	/// Disassembles a method body.
	/// </summary>
	sealed class MethodBodyDisassembler
	{
		readonly IDecompilerOutput output;
		readonly bool detectControlStructure;
		readonly DisassemblerOptions options;
		
		public MethodBodyDisassembler(IDecompilerOutput output, bool detectControlStructure, DisassemblerOptions options)
		{
			if (output == null)
				throw new ArgumentNullException("output");
			this.output = output;
			this.detectControlStructure = detectControlStructure;
			this.options = options;
		}
		
		public void Disassemble(MethodDef method, MethodDebugInfoBuilder builder, InstructionOperandConverter instructionOperandConverter)
		{
			// start writing IL code
			CilBody body = method.Body;
			uint codeSize = (uint)body.GetCodeSize();
			uint rva = (uint)method.RVA;

			if (options.ShowTokenAndRvaComments) {
				output.WriteLine(string.Format("// Header Size: {0} {1}", method.Body.HeaderSize, method.Body.HeaderSize == 1 ? "byte" : "bytes"), BoxedTextColor.Comment);
				output.WriteLine(string.Format("// Code Size: {0} (0x{0:X}) {1}", codeSize, codeSize == 1 ? "byte" : "bytes"), BoxedTextColor.Comment);
				if (body.LocalVarSigTok != 0) {
					output.Write("// LocalVarSig Token: ", BoxedTextColor.Comment);
					output.Write(string.Format("0x{0:X8}", body.LocalVarSigTok), new TokenReference(method.Module, body.LocalVarSigTok), DecompilerReferenceFlags.None, BoxedTextColor.Comment);
					output.Write(string.Format(" RID: {0}", body.LocalVarSigTok & 0xFFFFFF), BoxedTextColor.Comment);
					output.WriteLine();
				}
			}
			output.Write(".maxstack", BoxedTextColor.ILDirective);
			output.Write(" ", BoxedTextColor.Text);
			output.WriteLine(string.Format("{0}", body.MaxStack), BoxedTextColor.Number);
            if (method.DeclaringType.Module.EntryPoint == method)
                output.WriteLine (".entrypoint", BoxedTextColor.ILDirective);
			
			if (method.Body.HasVariables) {
				output.Write(".locals", BoxedTextColor.ILDirective);
				output.Write(" ", BoxedTextColor.Text);
				if (method.Body.InitLocals) {
					output.Write("init", BoxedTextColor.Keyword);
					output.Write(" ", BoxedTextColor.Text);
				}
				var bh1 = BracePairHelper.Create(output, "(", CodeBracesRangeFlags.Parentheses);
				output.WriteLine();
				output.IncreaseIndent();
				foreach (var v in method.Body.Variables) {
					var local = (SourceLocal)instructionOperandConverter.Convert(v);
					var bh2 = BracePairHelper.Create(output, "[", CodeBracesRangeFlags.SquareBrackets);
					bool hasName = !string.IsNullOrEmpty(local.Local.Name);
					if (hasName)
						output.Write(local.Local.Index.ToString(), BoxedTextColor.Number);
					else
						output.Write(local.Local.Index.ToString(), local, DecompilerReferenceFlags.Local | DecompilerReferenceFlags.Definition, BoxedTextColor.Number);
					bh2.Write("]");
					output.Write(" ", BoxedTextColor.Text);
					local.Type.WriteTo(output);
					if (hasName) {
						output.Write(" ", BoxedTextColor.Text);
						output.Write(DisassemblerHelpers.Escape(local.Name), local, DecompilerReferenceFlags.Local | DecompilerReferenceFlags.Definition, BoxedTextColor.Local);
					}
					if (local.Local.Index + 1 < method.Body.Variables.Count)
						output.Write(",", BoxedTextColor.Punctuation);
					output.WriteLine();
				}
				output.DecreaseIndent();
				bh1.Write(")");
				output.WriteLine();
			}
			output.WriteLine();

			uint baseRva = rva == 0 ? 0 : rva + method.Body.HeaderSize;
			long baseOffs = baseRva == 0 ? 0 : method.Module.ToFileOffset(baseRva) ?? 0;
			using (var byteReader = !options.ShowILBytes || options.CreateInstructionBytesReader == null ? null : options.CreateInstructionBytesReader(method)) {
				if (detectControlStructure && body.Instructions.Count > 0) {
					int index = 0;
					HashSet<uint> branchTargets = GetBranchTargets(body.Instructions);
					WriteStructureBody(body, new ILStructure(body), branchTargets, ref index, builder, instructionOperandConverter, method.Body.GetCodeSize(), baseRva, baseOffs, byteReader, method);
				}
				else {
					var instructions = method.Body.Instructions;
					for (int i = 0; i < instructions.Count; i++) {
						var inst = instructions[i];
						int startLocation;
						inst.WriteTo(output, options, baseRva, baseOffs, byteReader, method, instructionOperandConverter, out startLocation);

						if (builder != null) {
							var next = i + 1 < instructions.Count ? instructions[i + 1] : null;
							builder.Add(new SourceStatement(ILSpan.FromBounds(inst.Offset, next == null ? (uint)method.Body.GetCodeSize() : next.Offset), new TextSpan(startLocation, output.NextPosition - startLocation)));
						}

						output.WriteLine();
					}
					if (method.Body.HasExceptionHandlers) {
						output.WriteLine();
						foreach (var eh in method.Body.ExceptionHandlers) {
							eh.WriteTo(output, method);
							output.WriteLine();
						}
					}
				}
			}
		}
		
		HashSet<uint> GetBranchTargets(IEnumerable<Instruction> instructions)
		{
			HashSet<uint> branchTargets = new HashSet<uint>();
			foreach (var inst in instructions) {
				Instruction target = inst.Operand as Instruction;
				if (target != null)
					branchTargets.Add(target.Offset);
				IList<Instruction> targets = inst.Operand as IList<Instruction>;
				if (targets != null)
					foreach (Instruction t in targets)
						if (t != null)
							branchTargets.Add(t.Offset);
			}
			return branchTargets;
		}
		
		BracePairHelper WriteStructureHeader(ILStructure s)
		{
			BracePairHelper bh;
			switch (s.Type) {
				case ILStructureType.Loop:
					output.Write("// loop start", BoxedTextColor.Comment);
					if (s.LoopEntryPoint != null) {
						output.Write(" (head: ", BoxedTextColor.Comment);
						DisassemblerHelpers.WriteOffsetReference(output, s.LoopEntryPoint, null, BoxedTextColor.Comment);
						output.Write(")", BoxedTextColor.Comment);
					}
					output.WriteLine();
					bh = default(BracePairHelper);
					break;
				case ILStructureType.Try:
					output.WriteLine(".try", BoxedTextColor.ILDirective);
					bh = BracePairHelper.Create(output, "{", CodeBracesRangeFlags.TryBraces);
					output.WriteLine();
					break;
				case ILStructureType.Handler:
					CodeBracesRangeFlags bpk;
					switch (s.ExceptionHandler.HandlerType) {
						case ExceptionHandlerType.Catch:
						case ExceptionHandlerType.Filter:
							output.Write("catch", BoxedTextColor.Keyword);
							if (s.ExceptionHandler.CatchType != null) {
								output.Write(" ", BoxedTextColor.Text);
								s.ExceptionHandler.CatchType.WriteTo(output, ILNameSyntax.TypeName);
							}
							output.WriteLine();
							bpk = s.ExceptionHandler.HandlerType == ExceptionHandlerType.Catch ? CodeBracesRangeFlags.CatchBraces : CodeBracesRangeFlags.FilterBraces;
							break;
						case ExceptionHandlerType.Finally:
							output.WriteLine("finally", BoxedTextColor.Keyword);
							bpk = CodeBracesRangeFlags.FinallyBraces;
							break;
						case ExceptionHandlerType.Fault:
							output.WriteLine("fault", BoxedTextColor.Keyword);
							bpk = CodeBracesRangeFlags.FaultBraces;
							break;
						default:
							output.WriteLine(s.ExceptionHandler.HandlerType.ToString(), BoxedTextColor.Keyword);
							bpk= CodeBracesRangeFlags.OtherBlockBraces;
							break;
					}
					bh = BracePairHelper.Create(output, "{", bpk);
					output.WriteLine();
					break;
				case ILStructureType.Filter:
					output.WriteLine("filter", BoxedTextColor.Keyword);
					bh = BracePairHelper.Create(output, "{", CodeBracesRangeFlags.FilterBraces);
					output.WriteLine();
					break;
				default:
					throw new NotSupportedException();
			}
			output.IncreaseIndent();
			return bh;
		}
		
		void WriteStructureBody(CilBody body, ILStructure s, HashSet<uint> branchTargets, ref int index, MethodDebugInfoBuilder builder, InstructionOperandConverter instructionOperandConverter, int codeSize, uint baseRva, long baseOffs, IInstructionBytesReader byteReader, MethodDef method)
		{
			bool isFirstInstructionInStructure = true;
			bool prevInstructionWasBranch = false;
			int childIndex = 0;
			var instructions = body.Instructions;
			while (index < instructions.Count) {
				Instruction inst = instructions[index];
				if (inst.Offset >= s.EndOffset)
					break;
				uint offset = inst.Offset;
				if (childIndex < s.Children.Count && s.Children[childIndex].StartOffset <= offset && offset < s.Children[childIndex].EndOffset) {
					ILStructure child = s.Children[childIndex++];
					var bh = WriteStructureHeader(child);
					WriteStructureBody(body, child, branchTargets, ref index, builder, instructionOperandConverter, codeSize, baseRva, baseOffs, byteReader, method);
					WriteStructureFooter(child, bh);
				} else {
					if (!isFirstInstructionInStructure && (prevInstructionWasBranch || branchTargets.Contains(offset))) {
						output.WriteLine(); // put an empty line after branches, and in front of branch targets
					}
					int startLocation;
					inst.WriteTo(output, options, baseRva, baseOffs, byteReader, method, instructionOperandConverter, out startLocation);
					
					if (builder != null) {
						var next = index + 1 < instructions.Count ? instructions[index + 1] : null;
						builder.Add(new SourceStatement(ILSpan.FromBounds(inst.Offset, next == null ? (uint)codeSize : next.Offset), new TextSpan(startLocation, output.NextPosition - startLocation)));
					}
					
					output.WriteLine();
					
					prevInstructionWasBranch = inst.OpCode.FlowControl == FlowControl.Branch
						|| inst.OpCode.FlowControl == FlowControl.Cond_Branch
						|| inst.OpCode.FlowControl == FlowControl.Return
						|| inst.OpCode.FlowControl == FlowControl.Throw;
					
					index++;
				}
				isFirstInstructionInStructure = false;
			}
		}
		
		void WriteStructureFooter(ILStructure s, BracePairHelper bh)
		{
			output.DecreaseIndent();
			switch (s.Type) {
				case ILStructureType.Loop:
					output.WriteLine("// end loop", BoxedTextColor.Comment);
					break;
				case ILStructureType.Try:
					bh.Write("}");
					output.Write(" ", BoxedTextColor.Text);
					output.WriteLine("// end .try", BoxedTextColor.Comment);
					break;
				case ILStructureType.Handler:
					bh.Write("}");
					output.Write(" ", BoxedTextColor.Text);
					output.WriteLine("// end handler", BoxedTextColor.Comment);
					break;
				case ILStructureType.Filter:
					bh.Write("}");
					output.Write(" ", BoxedTextColor.Text);
					output.WriteLine("// end filter", BoxedTextColor.Comment);
					break;
				default:
					throw new NotSupportedException();
			}
		}
	}
}
