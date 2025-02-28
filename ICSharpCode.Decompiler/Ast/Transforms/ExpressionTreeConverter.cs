﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using dnlib.DotNet;
using dnSpy.Contracts.Text;
using ICSharpCode.Decompiler.ILAst;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;

//TODO: Verify that no ILSpans have been removed from this file

namespace ICSharpCode.Decompiler.Ast.Transforms {
	public sealed class ExpressionTreeConverter
	{
		#region static TryConvert method
		public static bool CouldBeExpressionTree(InvocationExpression expr, StringBuilder sb)
		{
			if (expr != null && expr.Arguments.Count == 2) {
				IMethod mr = expr.Annotation<IMethod>();
				return mr != null && mr.Name == "Lambda" && mr.DeclaringType != null && FullNameFactory.FullName(mr.DeclaringType, false, null, sb.Clear()) == "System.Linq.Expressions.Expression";
			}
			return false;
		}

		public static Expression TryConvert(DecompilerContext context, Expression expr, StringBuilder sb)
		{
			Expression converted = new ExpressionTreeConverter(context, sb).Convert(expr);
			if (converted != null) {
				converted.AddAnnotation(new ExpressionTreeLambdaAnnotation());
			}
			return converted;
		}
		#endregion

		readonly DecompilerContext context;
		Stack<LambdaExpression> activeLambdas = new Stack<LambdaExpression>();
		readonly StringBuilder stringBuilder;

		private ExpressionTreeConverter(DecompilerContext context, StringBuilder sb)
		{
			this.context = context;
			this.stringBuilder = sb;
		}

		#region Main Convert method
		Expression Convert(Expression expr)
		{
			InvocationExpression invocation = expr as InvocationExpression;
			if (invocation != null) {
				IMethod mr = invocation.Annotation<IMethod>();
				if (mr != null && mr.DeclaringType != null && FullNameFactory.FullName(mr.DeclaringType, false, null, stringBuilder.Clear()) == "System.Linq.Expressions.Expression") {
					switch (mr.Name) {
						case "Add":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Add, false);
						case "AddChecked":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Add, true);
						case "AddAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Add, false);
						case "AddAssignChecked":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Add, true);
						case "And":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.BitwiseAnd);
						case "AndAlso":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.ConditionalAnd);
						case "AndAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.BitwiseAnd);
						case "ArrayAccess":
						case "ArrayIndex":
							return ConvertArrayIndex(invocation);
						case "ArrayLength":
							return ConvertArrayLength(invocation);
						case "Assign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Assign);
						case "Call":
							return ConvertCall(invocation);
						case "Coalesce":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.NullCoalescing);
						case "Condition":
							return ConvertCondition(invocation);
						case "Constant":
							if (invocation.Arguments.Count >= 1)
								return invocation.Arguments.First().Clone();
							else
								return NotSupported(expr);
						case "Convert":
							return ConvertCast(invocation, false);
						case "ConvertChecked":
							return ConvertCast(invocation, true);
						case "Divide":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Divide);
						case "DivideAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Divide);
						case "Equal":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Equality);
						case "ExclusiveOr":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.ExclusiveOr);
						case "ExclusiveOrAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.ExclusiveOr);
						case "Field":
							return ConvertField(invocation);
						case "GreaterThan":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.GreaterThan);
						case "GreaterThanOrEqual":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.GreaterThanOrEqual);
						case "Invoke":
							return ConvertInvoke(invocation);
						case "Lambda":
							return ConvertLambda(invocation);
						case "LeftShift":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.ShiftLeft);
						case "LeftShiftAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.ShiftLeft);
						case "LessThan":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.LessThan);
						case "LessThanOrEqual":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.LessThanOrEqual);
						case "ListInit":
							return ConvertListInit(invocation);
						case "MemberInit":
							return ConvertMemberInit(invocation);
						case "Modulo":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Modulus);
						case "ModuloAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Modulus);
						case "Multiply":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Multiply, false);
						case "MultiplyChecked":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Multiply, true);
						case "MultiplyAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Multiply, false);
						case "MultiplyAssignChecked":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Multiply, true);
						case "Negate":
							return ConvertUnaryOperator(invocation, UnaryOperatorType.Minus, false);
						case "NegateChecked":
							return ConvertUnaryOperator(invocation, UnaryOperatorType.Minus, true);
						case "New":
							return ConvertNewObject(invocation);
						case "NewArrayBounds":
							return ConvertNewArrayBounds(invocation);
						case "NewArrayInit":
							return ConvertNewArrayInit(invocation);
						case "Not":
							return ConvertUnaryOperator(invocation, UnaryOperatorType.Not);
						case "NotEqual":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.InEquality);
						case "OnesComplement":
							return ConvertUnaryOperator(invocation, UnaryOperatorType.BitNot);
						case "Or":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.BitwiseOr);
						case "OrAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.BitwiseOr);
						case "OrElse":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.ConditionalOr);
						case "Property":
							return ConvertProperty(invocation);
						case "Quote":
							if (invocation.Arguments.Count == 1)
								return Convert(invocation.Arguments.Single());
							else
								return NotSupported(invocation);
						case "RightShift":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.ShiftRight);
						case "RightShiftAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.ShiftRight);
						case "Subtract":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Subtract, false);
						case "SubtractChecked":
							return ConvertBinaryOperator(invocation, BinaryOperatorType.Subtract, true);
						case "SubtractAssign":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Subtract, false);
						case "SubtractAssignChecked":
							return ConvertAssignmentOperator(invocation, AssignmentOperatorType.Subtract, true);
						case "TypeAs":
							return ConvertTypeAs(invocation);
						case "TypeIs":
							return ConvertTypeIs(invocation);
					}
				}
			}
			IdentifierExpression ident = expr as IdentifierExpression;
			if (ident != null) {
				ILVariable v = ident.Annotation<ILVariable>();
				if (v != null) {
					foreach (LambdaExpression lambda in activeLambdas) {
						foreach (ParameterDeclaration p in lambda.Parameters) {
							if (p.Annotation<ILVariable>() == v)
								return IdentifierExpression.Create(p.Name, v.IsParameter ? BoxedTextColor.Parameter : BoxedTextColor.Local).WithAnnotation(v);
						}
					}
				}
			}
			return NotSupported(expr);
		}

		Expression NotSupported(Expression expr)
		{
			Debug.WriteLine("Expression Tree Conversion Failed: '" + expr + "' is not supported");
			return null;
		}
		#endregion

		#region Convert Lambda
		static readonly Expression emptyArrayPattern = new ArrayCreateExpression {
			Type = new AnyNode(),
			Arguments = { new PrimitiveExpression(0) }
		};

		Expression ConvertLambda(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);
			LambdaExpression lambda = new LambdaExpression();
			Expression body = invocation.Arguments.First();
			ArrayCreateExpression parameterArray = invocation.Arguments.Last() as ArrayCreateExpression;
			if (parameterArray == null)
				return NotSupported(invocation);

			var annotation = body.Annotation<ParameterDeclarationAnnotation>();
			if (annotation != null) {
				lambda.Parameters.AddRange(annotation.GetParameters());
			} else {
				// No parameter declaration annotation found.
				if (!emptyArrayPattern.IsMatch(parameterArray))
					return null;
			}

			activeLambdas.Push(lambda);
			Expression convertedBody = Convert(body);
			activeLambdas.Pop();
			if (convertedBody == null) {
				lambda.Parameters.Clear();
				return null;
			}
			lambda.Body = convertedBody;
			return lambda;
		}
		#endregion

		#region Convert Field
		static readonly Expression getFieldFromHandlePattern =
			new TypePattern(typeof(FieldInfo)).ToType().Invoke2(
				BoxedTextColor.StaticMethod,
				"GetFieldFromHandle",
				new LdTokenPattern("field").ToExpression().Member("FieldHandle", BoxedTextColor.InstanceProperty),
				new OptionalNode(new TypeOfExpression(new AnyNode("declaringType")).Member("TypeHandle", BoxedTextColor.InstanceProperty))
			);

		Expression ConvertField(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			Expression fieldInfoExpr = invocation.Arguments.ElementAt(1);

			Match m = getFieldFromHandlePattern.Match(fieldInfoExpr);
			if (!m.Success) {

				if (fieldInfoExpr is CastExpression c) {
					fieldInfoExpr = c.Expression;
					m = getFieldFromHandlePattern.Match(fieldInfoExpr);

					if (!m.Success) {
						return NotSupported(invocation);
					}
				} else {
					return NotSupported(invocation);
				}

			}

			IField fr = m.Get<AstNode>("field").Single().Annotation<IField>();
			if (fr == null)
				return null;

			Expression target = invocation.Arguments.ElementAt(0);
			Expression convertedTarget;
			if (target is NullReferenceExpression) {
				if (m.Has("declaringType"))
					convertedTarget = new TypeReferenceExpression(m.Get<AstType>("declaringType").Single().Clone());
				else
					convertedTarget = new TypeReferenceExpression(AstBuilder.ConvertType(fr.DeclaringType, stringBuilder));
			} else {
				convertedTarget = Convert(target);
				if (convertedTarget == null)
					return null;
			}

			return convertedTarget.Member(fr.Name, fr).WithAnnotation(fr);
		}
		#endregion

		#region Convert Property
		static readonly Expression getMethodFromHandlePattern =
			new TypePattern(typeof(MethodBase)).ToType().Invoke2(
				BoxedTextColor.StaticMethod,
				"GetMethodFromHandle",
				new LdTokenPattern("method").ToExpression().Member("MethodHandle", BoxedTextColor.InstanceProperty),
				new OptionalNode(new TypeOfExpression(new AnyNode("declaringType")).Member("TypeHandle", BoxedTextColor.InstanceProperty))
			).CastTo(new TypePattern(typeof(MethodInfo)));

		Expression ConvertProperty(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			Expression methodExpr = invocation.Arguments.ElementAt(1);

			Match m = getMethodFromHandlePattern.Match(methodExpr);

			if (!m.Success) {
				if (methodExpr is CastExpression c) {
					methodExpr = c.Expression;

					m = getMethodFromHandlePattern.Match(methodExpr);

					if(!m.Success) {
						return NotSupported(invocation);
					}

				} else {
					return NotSupported(invocation);
				}
			}

			IMethod mr = m.Get<AstNode>("method").Single().Annotation<IMethod>();
			if (mr == null)
				return null;

			Expression target = invocation.Arguments.ElementAt(0);
			Expression convertedTarget;
			if (target is NullReferenceExpression) {
				if (m.Has("declaringType"))
					convertedTarget = new TypeReferenceExpression(m.Get<AstType>("declaringType").Single().Clone());
				else
					convertedTarget = new TypeReferenceExpression(AstBuilder.ConvertType(mr.DeclaringType, stringBuilder));
			} else {
				convertedTarget = Convert(target);
				if (convertedTarget == null)
					return null;
			}

			return convertedTarget.Member(GetPropertyName(mr), mr.MethodSig == null || mr.MethodSig.HasThis ? BoxedTextColor.InstanceProperty : BoxedTextColor.StaticProperty).WithAnnotation(mr);
		}

		string GetPropertyName(IMethod accessor)
		{
			string name = accessor.Name;
			if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
				name = name.Substring(4);
			return name;
		}
		#endregion

		#region Convert Call
		Expression ConvertCall(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count < 2)
				return NotSupported(invocation);

			Expression target;
			int firstArgumentPosition;

			Match m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(0));
			if (m.Success) {
				target = null;
				firstArgumentPosition = 1;
			} else {
				m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(1));
				if (!m.Success)
					return NotSupported(invocation);
				target = invocation.Arguments.ElementAt(0);
				firstArgumentPosition = 2;
			}

			IMethod mr = m.Get<AstNode>("method").Single().Annotation<IMethod>();
			if (mr == null)
				return null;

			Expression convertedTarget;
			if (target == null || target is NullReferenceExpression) {
				// static method
				if (m.Has("declaringType"))
					convertedTarget = new TypeReferenceExpression(m.Get<AstType>("declaringType").Single().Clone());
				else
					convertedTarget = new TypeReferenceExpression(AstBuilder.ConvertType(mr.DeclaringType, stringBuilder));
			} else {
				convertedTarget = Convert(target);
				if (convertedTarget == null)
					return null;
			}

			MemberReferenceExpression mre = convertedTarget.Member(mr.Name, mr);
			MethodSpec gim = mr as MethodSpec;
			if (gim != null && gim.GenericInstMethodSig != null) {
				for (int i = 0; i < gim.GenericInstMethodSig.GenericArguments.Count; i++) {
					mre.TypeArguments.Add(AstBuilder.ConvertType(gim.GenericInstMethodSig.GenericArguments[i], stringBuilder));
				}
			}
			IList<Expression> arguments = null;
			if (invocation.Arguments.Count == firstArgumentPosition + 1) {
				Expression argumentArray = invocation.Arguments.ElementAt(firstArgumentPosition);
				arguments = ConvertExpressionsArray(argumentArray);
			}
			if (arguments == null) {
				arguments = new List<Expression>();
				foreach (Expression argument in invocation.Arguments.Skip(firstArgumentPosition)) {
					if (argument is NullReferenceExpression) {

						if (m.Has("declaringType"))
							convertedTarget = new TypeReferenceExpression(m.Get<AstType>("declaringType").Single().Clone());
						else
							convertedTarget = new TypeReferenceExpression(AstBuilder.ConvertType(mr.DeclaringType, stringBuilder));

						arguments.Add(convertedTarget);
					} else {
						Expression convertedArgument = Convert(argument);

						if (convertedArgument == null)
							return null;

						arguments.Add(convertedArgument);
					}
				}
			}
			MethodDef methodDef = mr.ResolveMethodDef();
			if (methodDef != null) {
				if (methodDef.IsGetter) {
					PropertyDef indexer = AstMethodBodyBuilder.GetIndexer(methodDef);
					if (indexer != null)
						return new IndexerExpression(mre.Target.Detach(), arguments).WithAnnotation(indexer);
				}
			}
			else if (mr.Name == "get_Item")
				return new IndexerExpression(mre.Target.Detach(), arguments).WithAnnotation(mr);
			return new InvocationExpression(mre, arguments).WithAnnotation(mr);
		}

		Expression ConvertInvoke(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			Expression convertedTarget = Convert(invocation.Arguments.ElementAt(0));
			IList<Expression> convertedArguments = ConvertExpressionsArray(invocation.Arguments.ElementAt(1));
			if (convertedTarget != null && convertedArguments != null)
				return new InvocationExpression(convertedTarget, convertedArguments);
			else
				return null;
		}
		#endregion

		#region Convert Binary Operator
		static readonly Pattern trueOrFalse = new Choice {
			new PrimitiveExpression(true),
			new PrimitiveExpression(false)
		};

		Expression ConvertBinaryOperator(InvocationExpression invocation, BinaryOperatorType op, bool? isChecked = null)
		{
			if (invocation.Arguments.Count < 2)
				return NotSupported(invocation);

			Expression left = Convert(invocation.Arguments.ElementAt(0));
			if (left == null)
				return null;
			Expression right = Convert(invocation.Arguments.ElementAt(1));
			if (right == null)
				return null;

			BinaryOperatorExpression boe = new BinaryOperatorExpression(left, op, right);
			if (isChecked != null)
				boe.AddAnnotation(isChecked.Value ? AddCheckedBlocks.CheckedAnnotation : AddCheckedBlocks.UncheckedAnnotation);

			switch (invocation.Arguments.Count) {
				case 2:
					return boe;
				case 3:
					Match m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(2));
					if (m.Success)
						return boe.WithAnnotation(m.Get<AstNode>("method").Single().Annotation<IMethod>());
					else
						return null;
				case 4:
					if (!trueOrFalse.IsMatch(invocation.Arguments.ElementAt(2)))
						return null;
					m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(3));
					if (m.Success)
						return boe.WithAnnotation(m.Get<AstNode>("method").Single().Annotation<IMethod>());
					else if(invocation.Arguments.ElementAt(3).ToString() == "null")
						return boe;
					else
						return null;
				default:
					return NotSupported(invocation);
			}
		}
		#endregion

		#region Convert Assignment Operator
		Expression ConvertAssignmentOperator(InvocationExpression invocation, AssignmentOperatorType op, bool? isChecked = null)
		{
			return NotSupported(invocation);
		}
		#endregion

		#region Convert Unary Operator
		Expression ConvertUnaryOperator(InvocationExpression invocation, UnaryOperatorType op, bool? isChecked = null)
		{
			if (invocation.Arguments.Count < 1)
				return NotSupported(invocation);

			Expression expr = Convert(invocation.Arguments.ElementAt(0));
			if (expr == null)
				return null;

			UnaryOperatorExpression uoe = new UnaryOperatorExpression(op, expr);
			if (isChecked != null)
				uoe.AddAnnotation(isChecked.Value ? AddCheckedBlocks.CheckedAnnotation : AddCheckedBlocks.UncheckedAnnotation);

			switch (invocation.Arguments.Count) {
				case 1:
					return uoe;
				case 2:
					Match m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(1));
					if (m.Success)
						return uoe.WithAnnotation(m.Get<AstNode>("method").Single().Annotation<IMethod>());
					else
						return null;
				default:
					return NotSupported(invocation);
			}
		}
		#endregion

		#region Convert Condition Operator
		Expression ConvertCondition(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 3)
				return NotSupported(invocation);

			Expression condition = Convert(invocation.Arguments.ElementAt(0));
			Expression trueExpr = Convert(invocation.Arguments.ElementAt(1));
			Expression falseExpr = Convert(invocation.Arguments.ElementAt(2));
			if (condition != null && trueExpr != null && falseExpr != null)
				return new ConditionalExpression(condition, trueExpr, falseExpr);
			else
				return null;
		}
		#endregion

		#region Convert New Object
		static readonly Expression newObjectCtorPattern = new TypePattern(typeof(MethodBase)).ToType().Invoke2
			(
				BoxedTextColor.StaticMethod,
				"GetMethodFromHandle",
				new LdTokenPattern("ctor").ToExpression().Member("MethodHandle", BoxedTextColor.InstanceProperty),
				new OptionalNode(new TypeOfExpression(new AnyNode("declaringType")).Member("TypeHandle", BoxedTextColor.InstanceProperty))
			).CastTo(new TypePattern(typeof(ConstructorInfo)));

		Expression ConvertNewObject(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count < 1 || invocation.Arguments.Count > 3)
				return NotSupported(invocation);

			Match m = newObjectCtorPattern.Match(invocation.Arguments.First());
			if (!m.Success)
				return NotSupported(invocation);

			IMethod ctor = m.Get<AstNode>("ctor").Single().Annotation<IMethod>();
			if (ctor == null)
				return null;

			AstType declaringTypeNode;
			ITypeDefOrRef declaringType;
			if (m.Has("declaringType")) {
				declaringTypeNode = m.Get<AstType>("declaringType").Single().Clone();
				declaringType = declaringTypeNode.Annotation<ITypeDefOrRef>();
			} else {
				declaringTypeNode = AstBuilder.ConvertType(ctor.DeclaringType, stringBuilder);
				declaringType = ctor.DeclaringType;
			}
			if (declaringTypeNode == null)
				return null;

			ObjectCreateExpression oce = new ObjectCreateExpression(declaringTypeNode);
			if (invocation.Arguments.Count >= 2) {
				IList<Expression> arguments = ConvertExpressionsArray(invocation.Arguments.ElementAtOrDefault(1));
				if (arguments == null)
					return null;
				oce.Arguments.AddRange(arguments);
			}
			if (invocation.Arguments.Count >= 3 && declaringType.IsAnonymousType()) {
				MethodDef resolvedCtor = ctor.ResolveMethodDef();
				if (resolvedCtor == null)
					return null;
				int skip = resolvedCtor.Parameters.GetParametersSkip();
				if (resolvedCtor.Parameters.Count - skip != oce.Arguments.Count)
					return null;
				AnonymousTypeCreateExpression atce = new AnonymousTypeCreateExpression();
				var arguments = oce.Arguments.ToArray();
				if (AstMethodBodyBuilder.CanInferAnonymousTypePropertyNamesFromArguments(arguments, resolvedCtor.Parameters)) {
					oce.Arguments.MoveTo(atce.Initializers);
				} else {
					for (int i = 0; i < resolvedCtor.Parameters.Count - skip; i++) {
						atce.Initializers.Add(
							new NamedExpression {
								NameToken = Identifier.Create(resolvedCtor.Parameters[i + skip].Name).WithAnnotation(resolvedCtor.Parameters[i + skip]),
								Expression = arguments[i].Detach()
							});
					}
				}
				return atce;
			}

			return oce;
		}
		#endregion

		#region Convert ListInit
		static readonly Pattern elementInitArrayPattern = ArrayInitializationPattern(
			typeof(System.Linq.Expressions.ElementInit),
			new TypePattern(typeof(System.Linq.Expressions.Expression)).ToType().Invoke("ElementInit", new AnyNode("methodInfos"), new AnyNode("addArgumentsArrays"))
		);

		Expression ConvertListInit(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);
			ObjectCreateExpression oce = Convert(invocation.Arguments.ElementAt(0)) as ObjectCreateExpression;
			if (oce == null)
				return null;
			Expression elementsArray = invocation.Arguments.ElementAt(1);
			ArrayInitializerExpression initializer = ConvertElementInit(elementsArray);
			if (initializer != null) {
				oce.Initializer = initializer;
				return oce;
			} else {
				return null;
			}
		}

		ArrayInitializerExpression ConvertElementInit(Expression elementsArray)
		{
			IList<Expression> elements = ConvertExpressionsArray(elementsArray);
			if (elements != null) {
				return new ArrayInitializerExpression(elements);
			}
			Match m = elementInitArrayPattern.Match(elementsArray);
			if (!m.Success)
				return null;
			ArrayInitializerExpression result = new ArrayInitializerExpression();
			foreach (var elementInit in m.Get<Expression>("addArgumentsArrays")) {
				IList<Expression> arguments = ConvertExpressionsArray(elementInit);
				if (arguments == null)
					return null;
				result.Elements.Add(new ArrayInitializerExpression(arguments));
			}
			return result;
		}
		#endregion

		#region Convert MemberInit
		Expression ConvertMemberInit(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);
			ObjectCreateExpression oce = Convert(invocation.Arguments.ElementAt(0)) as ObjectCreateExpression;
			if (oce == null)
				return null;
			Expression elementsArray = invocation.Arguments.ElementAt(1);
			ArrayInitializerExpression bindings = ConvertMemberBindings(elementsArray);
			if (bindings == null)
				return null;
			oce.Initializer = bindings;
			return oce;
		}

		static readonly Pattern memberBindingArrayPattern = ArrayInitializationPattern(typeof(System.Linq.Expressions.MemberBinding), new AnyNode("binding"));
		static readonly INode expressionTypeReference = new TypeReferenceExpression(new TypePattern(typeof(System.Linq.Expressions.Expression)));

		ArrayInitializerExpression ConvertMemberBindings(Expression elementsArray)
		{
			Match m = memberBindingArrayPattern.Match(elementsArray);
			if (!m.Success)
				return null;
			ArrayInitializerExpression result = new ArrayInitializerExpression();
			foreach (var binding in m.Get<Expression>("binding")) {
				InvocationExpression bindingInvocation = binding as InvocationExpression;
				if (bindingInvocation == null || bindingInvocation.Arguments.Count != 2)
					return null;
				MemberReferenceExpression bindingMRE = bindingInvocation.Target as MemberReferenceExpression;
				if (bindingMRE == null || !expressionTypeReference.IsMatch(bindingMRE.Target))
					return null;

				Expression bindingTarget = bindingInvocation.Arguments.ElementAt(0);
				Expression bindingValue = bindingInvocation.Arguments.ElementAt(1);

				string memberName;
				object color;
				Match m2 = getMethodFromHandlePattern.Match(bindingTarget);
				if (m2.Success) {
					IMethod setter = m2.Get<AstNode>("method").Single().Annotation<IMethod>();
					if (setter == null)
						return null;
					memberName = GetPropertyName(setter);
					color = setter.MethodSig == null || setter.MethodSig.HasThis ? BoxedTextColor.InstanceProperty : BoxedTextColor.StaticProperty;
				} else {
					return null;
				}

				Expression convertedValue;
				switch (bindingMRE.MemberName) {
					case "Bind":
						convertedValue = Convert(bindingValue);
						break;
					case "MemberBind":
						convertedValue = ConvertMemberBindings(bindingValue);
						break;
					case "ListBind":
						convertedValue = ConvertElementInit(bindingValue);
						break;
					default:
						return null;
				}
				if (convertedValue == null)
					return null;
				result.Elements.Add(new NamedExpression(memberName, convertedValue, color));
			}
			return result;
		}
		#endregion

		#region Convert Cast
		Expression ConvertCast(InvocationExpression invocation, bool isChecked)
		{
			if (invocation.Arguments.Count < 2)
				return null;
			Expression converted = Convert(invocation.Arguments.ElementAt(0));
			AstType type = ConvertTypeReference(invocation.Arguments.ElementAt(1));
			if (converted != null && type != null) {
				CastExpression cast = converted.CastTo(type);
				cast.AddAnnotation(isChecked ? AddCheckedBlocks.CheckedAnnotation : AddCheckedBlocks.UncheckedAnnotation);
				switch (invocation.Arguments.Count) {
					case 2:
						return cast;
					case 3:
						Match m = getMethodFromHandlePattern.Match(invocation.Arguments.ElementAt(2));
						if (m.Success)
							return cast.WithAnnotation(m.Get<AstNode>("method").Single().Annotation<IMethod>());
						else
							return null;
				}
			}
			return null;
		}
		#endregion

		#region ConvertExpressionsArray
		static Pattern ArrayInitializationPattern(Type arrayElementType, INode elementPattern)
		{
			return new Choice {
				new ArrayCreateExpression {
					Type = new TypePattern(arrayElementType),
					Arguments = { new PrimitiveExpression(0) }
				},
				new ArrayCreateExpression {
					Type = new TypePattern(arrayElementType),
					AdditionalArraySpecifiers = { new ArraySpecifier() },
					Initializer = new ArrayInitializerExpression {
						Elements = { new Repeat(elementPattern) }
					}
				}
			};
		}

		static readonly Pattern expressionArrayPattern = ArrayInitializationPattern(typeof(System.Linq.Expressions.Expression), new AnyNode("elements"));

		IList<Expression> ConvertExpressionsArray(Expression arrayExpression)
		{
			Match m = expressionArrayPattern.Match(arrayExpression);
			if (m.Success) {
				List<Expression> result = new List<Expression>();
				foreach (Expression expr in m.Get<Expression>("elements")) {
					Expression converted = Convert(expr);
					if (converted == null)
						return null;
					result.Add(converted);
				}
				return result;
			}
			return null;
		}
		#endregion

		#region Convert TypeAs/TypeIs
		static readonly TypeOfPattern typeOfPattern = new TypeOfPattern("type");

		AstType ConvertTypeReference(Expression typeOfExpression)
		{
			Match m = typeOfPattern.Match(typeOfExpression);
			if (m.Success)
				return m.Get<AstType>("type").Single().Clone();
			else
				return null;
		}

		Expression ConvertTypeAs(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return null;
			Expression converted = Convert(invocation.Arguments.ElementAt(0));
			AstType type = ConvertTypeReference(invocation.Arguments.ElementAt(1));
			if (converted != null && type != null)
				return new AsExpression(converted, type);
			return null;
		}

		Expression ConvertTypeIs(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return null;
			Expression converted = Convert(invocation.Arguments.ElementAt(0));
			AstType type = ConvertTypeReference(invocation.Arguments.ElementAt(1));
			if (converted != null && type != null)
				return new IsExpression { Expression = converted, Type = type };
			return null;
		}
		#endregion

		#region Convert Array
		Expression ConvertArrayIndex(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			Expression targetConverted = Convert(invocation.Arguments.First());
			if (targetConverted == null)
				return null;

			Expression index = invocation.Arguments.ElementAt(1);
			Expression indexConverted = Convert(index);
			if (indexConverted != null) {
				return new IndexerExpression(targetConverted, indexConverted);
			}
			IList<Expression> indexesConverted = ConvertExpressionsArray(index);
			if (indexesConverted != null) {
				return new IndexerExpression(targetConverted, indexesConverted);
			}
			return null;
		}

		Expression ConvertArrayLength(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 1)
				return NotSupported(invocation);

			Expression targetConverted = Convert(invocation.Arguments.Single());
			if (targetConverted != null)
				return targetConverted.Member("Length", BoxedTextColor.InstanceProperty).WithAnnotation(Create_SystemArray_get_Length());
			else
				return null;
		}

		ModuleDef GetModule()
		{
			if (context.CurrentMethod != null && context.CurrentMethod.Module != null)
				return context.CurrentMethod.Module;
			if (context.CurrentType != null && context.CurrentType.Module != null)
				return context.CurrentType.Module;
			if (context.CurrentModule != null)
				return context.CurrentModule;

			return null;
		}

		IMDTokenProvider Create_SystemArray_get_Length()
		{
			if (Create_SystemArray_get_Length_result_initd)
				return Create_SystemArray_get_Length_result;
			Create_SystemArray_get_Length_result_initd = true;

			var module = GetModule();
			if (module == null)
				return null;

			const string propName = "Length";
			var type = module.CorLibTypes.GetTypeRef("System", "Array");
			var retType = module.CorLibTypes.Int32;
			var mr = new MemberRefUser(module, "get_" + propName, MethodSig.CreateInstance(retType), type);
			Create_SystemArray_get_Length_result = mr;
			var md = mr.ResolveMethod();
			if (md == null || md.DeclaringType == null)
				return mr;
			var prop = md.DeclaringType.FindProperty(propName);
			if (prop == null)
				return mr;

			Create_SystemArray_get_Length_result = prop;
			return prop;
		}
		IMDTokenProvider Create_SystemArray_get_Length_result;
		bool Create_SystemArray_get_Length_result_initd;

		Expression ConvertNewArrayInit(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			AstType elementType = ConvertTypeReference(invocation.Arguments.ElementAt(0));
			IList<Expression> elements = ConvertExpressionsArray(invocation.Arguments.ElementAt(1));
			if (elementType != null && elements != null) {
				if (ContainsAnonymousType(elementType)) {
					elementType = null;
				}
				return new ArrayCreateExpression {
					Type = elementType,
					AdditionalArraySpecifiers = { new ArraySpecifier() },
					Initializer = new ArrayInitializerExpression(elements)
				};
			}
			return null;
		}

		Expression ConvertNewArrayBounds(InvocationExpression invocation)
		{
			if (invocation.Arguments.Count != 2)
				return NotSupported(invocation);

			AstType elementType = ConvertTypeReference(invocation.Arguments.ElementAt(0));
			IList<Expression> arguments = ConvertExpressionsArray(invocation.Arguments.ElementAt(1));
			if (elementType != null && arguments != null) {
				if (ContainsAnonymousType(elementType)) {
					elementType = null;
				}
				ArrayCreateExpression ace = new ArrayCreateExpression();
				ace.Type = elementType;
				ace.Arguments.AddRange(arguments);
				return ace;
			}
			return null;
		}

		bool ContainsAnonymousType(AstType type)
		{
			foreach (AstType t in type.DescendantsAndSelf.OfType<AstType>()) {
				ITypeDefOrRef tr = t.Annotation<ITypeDefOrRef>();
				if (tr != null && tr.IsAnonymousType())
					return true;
			}
			return false;
		}
		#endregion
	}
}
