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
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;

namespace ICSharpCode.Decompiler.Ast {
	public static class NRefactoryExtensions
	{
		public static T WithAnnotation<T>(this T node, object annotation) where T : AstNode
		{
			if (annotation != null)
				node.AddAnnotation(annotation);
			return node;
		}
		
		public static T CopyAnnotationsFrom<T>(this T node, AstNode other) where T : AstNode
		{
			foreach (object annotation in other.Annotations) {
				node.AddAnnotation(annotation);
			}
			return node;
		}
		
		public static T Detach<T>(this T node) where T : AstNode
		{
			node.Remove();
			return node;
		}
		
		public static Expression WithName(this Expression node, string patternGroupName)
		{
			return new NamedNode(patternGroupName, node);
		}
		
		public static Statement WithName(this Statement node, string patternGroupName)
		{
			return new NamedNode(patternGroupName, node);
		}
		
		public static void AddNamedArgument(this NRefactory.CSharp.Attribute attribute, ModuleDef module, Type attrType, AssemblyRef attrTypeAssemblyRef, Type fieldType, AssemblyRef fieldTypeAssemblyRef, string fieldName, Expression argument)
		{
			var ide = new IdentifierExpression(fieldName);
			if (module != null) {
				if (attrTypeAssemblyRef == null)
					attrTypeAssemblyRef = module.CorLibTypes.AssemblyRef;
				if (fieldTypeAssemblyRef == null)
					fieldTypeAssemblyRef = module.CorLibTypes.AssemblyRef;
				TypeSig sig = module.CorLibTypes.GetCorLibTypeSig(module.Import(fieldType));
				if (sig == null) {
					var typeRef = module.UpdateRowId(new TypeRefUser(module, fieldType.Namespace, fieldType.Name, fieldTypeAssemblyRef));
					sig = fieldType.IsValueType ? (TypeSig)new ValueTypeSig(typeRef) : new ClassSig(typeRef);
				}
				var fr = new MemberRefUser(module, fieldName, new FieldSig(sig), module.UpdateRowId(new TypeRefUser(module, attrType.Namespace, attrType.Name, attrTypeAssemblyRef)));
				ide.AddAnnotation(fr);
				ide.IdentifierToken.AddAnnotation(fr);
			}

			attribute.Arguments.Add(new AssignmentExpression(ide, argument));
		}
		
		public static AstType ToType(this Pattern pattern)
		{
			return pattern;
		}
		
		public static Expression ToExpression(this Pattern pattern)
		{
			return pattern;
		}
		
		public static Statement ToStatement(this Pattern pattern)
		{
			return pattern;
		}
		
		public static Statement GetNextStatement(this Statement statement)
		{
			AstNode next = statement.NextSibling;
			while (next != null && !(next is Statement))
				next = next.NextSibling;
			return (Statement)next;
		}

		public static void AddAllRecursiveBinSpansTo(this AstNode node, AstNode target)
		{
			if (node == null)
				return;
			var binSpans = node.GetAllRecursiveBinSpans();
			if (binSpans.Count > 0)
				target.AddAnnotation(binSpans);
		}

		public static void AddAllRecursiveBinSpansTo(this IEnumerable<AstNode> nodes, AstNode target)
		{
			if (nodes == null)
				return;
			var binSpans = nodes.GetAllRecursiveBinSpans();
			if (binSpans.Count > 0)
				target.AddAnnotation(binSpans);
		}

		public static List<BinSpan> GetAllRecursiveBinSpans(this AstNode node)
		{
			if (node == null)
				return new List<BinSpan>();

			var binSpans = new List<BinSpan>();
			foreach (var d in node.DescendantsAndSelf)
				d.GetAllBinSpans(binSpans);
			return binSpans;
		}

		public static List<BinSpan> GetAllRecursiveBinSpans(this IEnumerable<AstNode> nodes)
		{
			if (nodes == null)
				return new List<BinSpan>();

			var binSpans = new List<BinSpan>();
			foreach (var node in nodes) {
				foreach (var d in node.DescendantsAndSelf)
					d.GetAllBinSpans(binSpans);
			}
			return binSpans;
		}

		public static List<BinSpan> GetAllBinSpans(this AstNode node)
		{
			if (node == null)
				return new List<BinSpan>();

			var binSpans = new List<BinSpan>();
			node.GetAllBinSpans(binSpans);
			return binSpans;
		}

		static void GetAllBinSpans(this AstNode node, List<BinSpan> binSpans)
		{
			if (node == null)
				return;
			var block = node as BlockStatement;
			if (block != null) {
				binSpans.AddRange(block.HiddenStart.GetAllRecursiveBinSpans());
				binSpans.AddRange(block.HiddenEnd.GetAllRecursiveBinSpans());
			}
			var fe = node as ForeachStatement;
			if (fe != null) {
				binSpans.AddRange(fe.HiddenGetCurrentNode.GetAllRecursiveBinSpans());
				binSpans.AddRange(fe.HiddenMoveNextNode.GetAllRecursiveBinSpans());
				binSpans.AddRange(fe.HiddenGetEnumeratorNode.GetAllRecursiveBinSpans());
			}
			var sw = node as SwitchStatement;
			if (sw != null)
				binSpans.AddRange(sw.HiddenEnd.GetAllRecursiveBinSpans());
			foreach (var ann in node.Annotations) {
				var list = ann as IList<BinSpan>;
				if (list != null)
					binSpans.AddRange(list);
			}
		}

		public static AstNode CreateHidden(List<BinSpan> list, AstNode stmt)
		{
			if (list == null || list.Count == 0)
				return stmt;
			if (stmt == null)
				stmt = new EmptyStatement();
			stmt.AddAnnotation(list);
			return stmt;
		}

		public static AstNode CreateHidden(AstNode stmt, params AstNode[] otherNodes)
		{
			var list = new List<BinSpan>();
			foreach (var node in otherNodes) {
				if (node == null)
					continue;
				list.AddRange(node.GetAllRecursiveBinSpans());
			}
			if (list.Count > 0) {
				if (stmt == null)
					stmt = new EmptyStatement();
				stmt.AddAnnotation(list);
			}
			return stmt;
		}

		public static void RemoveAllBinSpansRecursive(this AstNode node)
		{
			foreach (var d in node.DescendantsAndSelf)
				d.RemoveAnnotations(typeof(IList<BinSpan>));
		}
	}
}
