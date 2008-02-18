//
// expression.cs: Expression representation for the IL tree.
//
// Author:
//   Miguel de Icaza (miguel@ximian.com)
//   Marek Safar (marek.safar@seznam.cz)
//
// (C) 2001, 2002, 2003 Ximian, Inc.
// (C) 2003, 2004 Novell, Inc.
//
#define USE_OLD

namespace Mono.CSharp {
	using System;
	using System.Collections;
	using System.Reflection;
	using System.Reflection.Emit;
	using System.Text;

	//
	// This is an user operator expression, automatically created during
	// resolve phase
	//
	public class UserOperatorCall : Expression {
		public delegate Expression ExpressionTreeExpression (EmitContext ec, MethodGroupExpr mg);

		readonly ArrayList arguments;
		readonly MethodGroupExpr mg;
		readonly ExpressionTreeExpression expr_tree;

		public UserOperatorCall (MethodGroupExpr mg, ArrayList args, ExpressionTreeExpression expr_tree, Location loc)
		{
			this.mg = mg;
			this.arguments = args;
			this.expr_tree = expr_tree;

			type = ((MethodInfo)mg).ReturnType;
			eclass = ExprClass.Value;
			this.loc = loc;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			if (expr_tree != null)
				return expr_tree (ec, mg);

			ArrayList args = new ArrayList (arguments.Count + 1);
			args.Add (new Argument (new NullConstant (loc).CreateExpressionTree (ec)));
			args.Add (new Argument (mg.CreateExpressionTree (ec)));
			foreach (Argument a in arguments) {
				args.Add (new Argument (a.Expr.CreateExpressionTree (ec)));
			}

			return CreateExpressionFactoryCall ("Call", args);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			//
			// We are born fully resolved
			//
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			mg.EmitCall (ec, arguments);
		}

		[Obsolete ("It may not be compatible with expression trees")]
		static public UserOperatorCall MakeSimpleCall (EmitContext ec, MethodGroupExpr mg,
							 Expression e, Location loc)
		{
			ArrayList args;
			
			args = new ArrayList (1);
			Argument a = new Argument (e, Argument.AType.Expression);

                        // We need to resolve the arguments before sending them in !
                        if (!a.Resolve (ec, loc))
                                return null;

                        args.Add (a);
			mg = mg.OverloadResolve (ec, ref args, false, loc);

			if (mg == null)
				return null;

			return new UserOperatorCall (mg, args, null, loc);
		}

		public MethodGroupExpr Method {
			get { return mg; }
		}
	}

	public class ParenthesizedExpression : Expression
	{
		public Expression Expr;

		public ParenthesizedExpression (Expression expr)
		{
			this.Expr = expr;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			Expr = Expr.Resolve (ec);
			return Expr;
		}

		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Should not happen");
		}

		public override Location Location
		{
			get {
				return Expr.Location;
			}
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			ParenthesizedExpression target = (ParenthesizedExpression) t;

			target.Expr = Expr.Clone (clonectx);
		}
	}
	
	//
	//   Unary implements unary expressions.
	//
	public class Unary : Expression {
		public enum Operator : byte {
			UnaryPlus, UnaryNegation, LogicalNot, OnesComplement,
			Indirection, AddressOf,  TOP
		}

		public readonly Operator Oper;
		public Expression Expr;

		public Unary (Operator op, Expression expr, Location loc)
		{
			this.Oper = op;
			this.Expr = expr;
			this.loc = loc;
		}

		/// <summary>
		///   Returns a stringified representation of the Operator
		/// </summary>
		static public string OperName (Operator oper)
		{
			switch (oper){
			case Operator.UnaryPlus:
				return "+";
			case Operator.UnaryNegation:
				return "-";
			case Operator.LogicalNot:
				return "!";
			case Operator.OnesComplement:
				return "~";
			case Operator.AddressOf:
				return "&";
			case Operator.Indirection:
				return "*";
			}

			return oper.ToString ();
		}

		public static readonly string [] oper_names;

		static Unary ()
		{
			oper_names = new string [(int)Operator.TOP];

			oper_names [(int) Operator.UnaryPlus] = "op_UnaryPlus";
			oper_names [(int) Operator.UnaryNegation] = "op_UnaryNegation";
			oper_names [(int) Operator.LogicalNot] = "op_LogicalNot";
			oper_names [(int) Operator.OnesComplement] = "op_OnesComplement";
			oper_names [(int) Operator.Indirection] = "op_Indirection";
			oper_names [(int) Operator.AddressOf] = "op_AddressOf";
		}

		public static void Error_OperatorCannotBeApplied (Location loc, string oper, Type t)
		{
			Error_OperatorCannotBeApplied (loc, oper, TypeManager.CSharpName (t));
		}

		public static void Error_OperatorCannotBeApplied (Location loc, string oper, string type)
		{
			Report.Error (23, loc, "The `{0}' operator cannot be applied to operand of type `{1}'",
				oper, type);
		}

		void Error23 (Type t)
		{
			Error_OperatorCannotBeApplied (loc, OperName (Oper), t);
		}

		// <summary>
		//   This routine will attempt to simplify the unary expression when the
		//   argument is a constant.
		// </summary>
		Constant TryReduceConstant (EmitContext ec, Constant e)
		{
			Type expr_type = e.Type;
			
			switch (Oper){
				case Operator.UnaryPlus:
					// Unary numeric promotions
					if (expr_type == TypeManager.byte_type)
						return new IntConstant (((ByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.sbyte_type)
						return new IntConstant (((SByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.short_type)
						return new IntConstant (((ShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.ushort_type)
						return new IntConstant (((UShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.char_type)
						return new IntConstant (((CharConstant)e).Value, e.Location);

					// Predefined operators
					if (expr_type == TypeManager.int32_type || expr_type == TypeManager.uint32_type ||
						expr_type == TypeManager.int64_type || expr_type == TypeManager.uint64_type ||
						expr_type == TypeManager.float_type || expr_type == TypeManager.double_type ||
						expr_type == TypeManager.decimal_type)
					{
						return e;
					}

					return null;
				
				case Operator.UnaryNegation:
					// Unary numeric promotions
					if (expr_type == TypeManager.byte_type)
						return new IntConstant (-((ByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.sbyte_type)
						return new IntConstant (-((SByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.short_type)
						return new IntConstant (-((ShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.ushort_type)
						return new IntConstant (-((UShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.char_type)
						return new IntConstant (-((CharConstant)e).Value, e.Location);

					// Predefined operators
					if (expr_type == TypeManager.int32_type) {
						int value = ((IntConstant)e).Value;
						if (value == int.MinValue) {
							if (ec.ConstantCheckState) {
								ConstantFold.Error_CompileTimeOverflow (loc);
								return null;
							}
							return e;
						}
						return new IntConstant (-value, e.Location);
					}
					if (expr_type == TypeManager.int64_type) {
						long value = ((LongConstant)e).Value;
						if (value == long.MinValue) {
							if (ec.ConstantCheckState) {
								ConstantFold.Error_CompileTimeOverflow (loc);
								return null;
							}
							return e;
						}
						return new LongConstant (-value, e.Location);
					}

					if (expr_type == TypeManager.uint32_type) {
						UIntLiteral uil = e as UIntLiteral;
						if (uil != null) {
							if (uil.Value == 2147483648)
								return new IntLiteral (int.MinValue, e.Location);
							return new LongLiteral (-uil.Value, e.Location);
						}
						return new LongConstant (-((UIntConstant)e).Value, e.Location);
					}

					if (expr_type == TypeManager.uint64_type) {
						ULongLiteral ull = e as ULongLiteral;
						if (ull != null && ull.Value == 9223372036854775808)
							return new LongLiteral (long.MinValue, e.Location);
						return null;
					}

					if (expr_type == TypeManager.float_type) {
						FloatLiteral fl = e as FloatLiteral;
						// For better error reporting
						if (fl != null) {
							fl.Value = -fl.Value;
							return fl;
						}
						return new FloatConstant (-((FloatConstant)e).Value, e.Location);
					}
					if (expr_type == TypeManager.double_type) {
						DoubleLiteral dl = e as DoubleLiteral;
						// For better error reporting
						if (dl != null) {
							dl.Value = -dl.Value;
							return dl;
						}

						return new DoubleConstant (-((DoubleConstant)e).Value, e.Location);
					}
					if (expr_type == TypeManager.decimal_type)
						return new DecimalConstant (-((DecimalConstant)e).Value, e.Location);

					return null;
				
				case Operator.LogicalNot:
					if (expr_type != TypeManager.bool_type)
						return null;
					
					BoolConstant b = (BoolConstant) e;
					return new BoolConstant (!(b.Value), b.Location);
				
				case Operator.OnesComplement:
					// Unary numeric promotions
					if (expr_type == TypeManager.byte_type)
						return new IntConstant (~((ByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.sbyte_type)
						return new IntConstant (~((SByteConstant)e).Value, e.Location);
					if (expr_type == TypeManager.short_type)
						return new IntConstant (~((ShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.ushort_type)
						return new IntConstant (~((UShortConstant)e).Value, e.Location);
					if (expr_type == TypeManager.char_type)
						return new IntConstant (~((CharConstant)e).Value, e.Location);

					// Predefined operators
					if (expr_type == TypeManager.int32_type)
						return new IntConstant (~((IntConstant)e).Value, e.Location);
					if (expr_type == TypeManager.uint32_type)
						return new UIntConstant (~((UIntConstant)e).Value, e.Location);
					if (expr_type == TypeManager.int64_type)
						return new LongConstant (~((LongConstant)e).Value, e.Location);
					if (expr_type == TypeManager.uint64_type){
						return new ULongConstant (~((ULongConstant)e).Value, e.Location);
					}
					if (e is EnumConstant) {
						e = TryReduceConstant (ec, ((EnumConstant)e).Child);
						if (e != null)
							e = new EnumConstant (e, expr_type);
						return e;
					}
					return null;

				case Operator.AddressOf:
					return e;

				case Operator.Indirection:
					return e;
			}
			throw new Exception ("Can not constant fold: " + Oper.ToString());
		}

		Expression ResolveOperator (EmitContext ec)
		{
			//
			// Step 1: Default operations on CLI native types.
			//

			// Attempt to use a constant folding operation.
			Constant cexpr = Expr as Constant;
			if (cexpr != null) {
				cexpr = TryReduceConstant (ec, cexpr);
				if (cexpr != null) {
					return cexpr;
				}
			}

			//
			// Step 2: Perform Operator Overload location
			//
			Type expr_type = Expr.Type;
			string op_name = oper_names [(int) Oper];

			MethodGroupExpr user_op = MemberLookup (ec.ContainerType, expr_type, op_name, MemberTypes.Method, AllBindingFlags, loc) as MethodGroupExpr;
			if (user_op != null) {
				ArrayList args = new ArrayList (1);
				args.Add (new Argument (Expr));
				user_op = user_op.OverloadResolve (ec, ref args, false, loc);

				if (user_op == null) {
					Error23 (expr_type);
					return null;
				}

				return new UserOperatorCall (user_op, args, CreateExpressionTree, loc);
			}

			switch (Oper){
			case Operator.LogicalNot:
				if (expr_type != TypeManager.bool_type) {
					Expr = ResolveBoolean (ec, Expr, loc);
					if (Expr == null){
						Error23 (expr_type);
						return null;
					}
				}
				
				type = TypeManager.bool_type;
				return this;

			case Operator.OnesComplement:
				// Unary numeric promotions
				if (expr_type == TypeManager.byte_type || expr_type == TypeManager.sbyte_type ||
					expr_type == TypeManager.short_type || expr_type == TypeManager.ushort_type ||
					expr_type == TypeManager.char_type) 
				{
					type = TypeManager.int32_type;
					return EmptyCast.Create (this, type);
				}

				// Predefined operators
				if (expr_type == TypeManager.int32_type || expr_type == TypeManager.uint32_type ||
					expr_type == TypeManager.int64_type || expr_type == TypeManager.uint64_type ||
					TypeManager.IsEnumType (expr_type))
				{
					type = expr_type;
					return this;
				}

				type = TypeManager.int32_type;
				Expr = Convert.ImplicitUserConversion(ec, Expr, type, loc);
				if (Expr != null)
					return this;

				Error23 (expr_type);
				return null;

			case Operator.AddressOf:
				if (!ec.InUnsafe) {
					UnsafeError (loc); 
					return null;
				}
				
				if (!TypeManager.VerifyUnManaged (Expr.Type, loc)){
					return null;
				}

				IVariable variable = Expr as IVariable;
				bool is_fixed = variable != null && variable.VerifyFixed ();

				if (!ec.InFixedInitializer && !is_fixed) {
					Error (212, "You can only take the address of unfixed expression inside " +
					       "of a fixed statement initializer");
					return null;
				}

				if (ec.InFixedInitializer && is_fixed) {
					Error (213, "You cannot use the fixed statement to take the address of an already fixed expression");
					return null;
				}

				LocalVariableReference lr = Expr as LocalVariableReference;
				if (lr != null){
					if (lr.local_info.IsCaptured){
						AnonymousMethod.Error_AddressOfCapturedVar (lr.Name, loc);
						return null;
					}
					lr.local_info.AddressTaken = true;
					lr.local_info.Used = true;
				}

				ParameterReference pr = Expr as ParameterReference;
				if ((pr != null) && pr.Parameter.IsCaptured) {
					AnonymousMethod.Error_AddressOfCapturedVar (pr.Name, loc);
					return null;
				}

				// According to the specs, a variable is considered definitely assigned if you take
				// its address.
				if ((variable != null) && (variable.VariableInfo != null)){
					variable.VariableInfo.SetAssigned (ec);
				}

				type = TypeManager.GetPointerType (Expr.Type);
				return this;

			case Operator.Indirection:
				if (!ec.InUnsafe){
					UnsafeError (loc);
					return null;
				}
				
				if (!expr_type.IsPointer){
					Error (193, "The * or -> operator must be applied to a pointer");
					return null;
				}
				
				//
				// We create an Indirection expression, because
				// it can implement the IMemoryLocation.
				// 
				return new Indirection (Expr, loc);
			
			case Operator.UnaryPlus:
				// Unary numeric promotions
				if (expr_type == TypeManager.byte_type || expr_type == TypeManager.sbyte_type ||
					expr_type == TypeManager.short_type || expr_type == TypeManager.ushort_type ||
					expr_type == TypeManager.char_type) 
				{
					return EmptyCast.Create (Expr, TypeManager.int32_type);
				}

				// Predefined operators
				if (expr_type == TypeManager.int32_type || expr_type == TypeManager.uint32_type ||
					expr_type == TypeManager.int64_type || expr_type == TypeManager.uint64_type ||
					expr_type == TypeManager.float_type || expr_type == TypeManager.double_type ||
					expr_type == TypeManager.decimal_type)
				{
					return Expr;
				}

				Expr = Convert.ImplicitUserConversion(ec, Expr, TypeManager.int32_type, loc);
				if (Expr != null) {
					 // Because we can completely ignore unary +
					return Expr;
				}

				Error23 (expr_type);
				return null;

			case Operator.UnaryNegation:
				//
				// transform - - expr into expr
				//
				Unary u = Expr as Unary;
				if (u != null && u.Oper == Operator.UnaryNegation) {
					return u.Expr;
				}

				// Unary numeric promotions
				if (expr_type == TypeManager.byte_type || expr_type == TypeManager.sbyte_type ||
					expr_type == TypeManager.short_type || expr_type == TypeManager.ushort_type ||
					expr_type == TypeManager.char_type) 
				{
					type = TypeManager.int32_type;
					return EmptyCast.Create (this, type);
				}

				//
				// Predefined operators
				//
				if (expr_type == TypeManager.uint32_type) {
					type = TypeManager.int64_type;
					Expr = Convert.ImplicitNumericConversion (Expr, type);
					return this;
				}

				if (expr_type == TypeManager.int32_type || expr_type == TypeManager.int64_type || 
					expr_type == TypeManager.float_type || expr_type == TypeManager.double_type ||
					expr_type == TypeManager.decimal_type)
				{
					type = expr_type;
					return this;
				}

				//
				// User conversion

				type = TypeManager.int32_type;
				Expr = Convert.ImplicitUserConversion(ec, Expr, type, loc);
				if (Expr != null)
					return this;

				Error23 (expr_type);
				return null;
			}

			Error (187, "No such operator '" + OperName (Oper) + "' defined for type '" +
			       TypeManager.CSharpName (expr_type) + "'");
			return null;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			return CreateExpressionTree (ec, null);
		}

		Expression CreateExpressionTree (EmitContext ec, MethodGroupExpr user_op)
		{
			string method_name; 
			switch (Oper) {
				case Operator.UnaryNegation:
					method_name = "Negate";
					break;

				default:
					throw new InternalErrorException ("Unknown unary operator " + Oper.ToString ());
			}

			ArrayList args = new ArrayList (2);
			args.Add (new Argument (Expr.CreateExpressionTree (ec)));
			if (user_op != null)
				args.Add (new Argument (user_op.CreateExpressionTree (ec)));
			return CreateExpressionFactoryCall (method_name, args);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (Oper == Operator.AddressOf) {
				Expr = Expr.DoResolveLValue (ec, new EmptyExpression ());

				if (Expr == null || Expr.eclass != ExprClass.Variable){
					Error (211, "Cannot take the address of the given expression");
					return null;
				}
			}
			else
				Expr = Expr.Resolve (ec);

			if (Expr == null)
				return null;

#if GMCS_SOURCE
			if (TypeManager.IsNullableValueType (Expr.Type))
				return new Nullable.LiftedUnaryOperator (Oper, Expr, loc).Resolve (ec);
#endif

			eclass = ExprClass.Value;
			return ResolveOperator (ec);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right)
		{
			if (Oper == Operator.Indirection)
				return DoResolve (ec);

			return null;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			switch (Oper) {
			case Operator.UnaryPlus:
				throw new Exception ("This should be caught by Resolve");
				
			case Operator.UnaryNegation:
				if (ec.CheckState && type != TypeManager.float_type && type != TypeManager.double_type) {
					ig.Emit (OpCodes.Ldc_I4_0);
					if (type == TypeManager.int64_type)
						ig.Emit (OpCodes.Conv_U8);
					Expr.Emit (ec);
					ig.Emit (OpCodes.Sub_Ovf);
				} else {
					Expr.Emit (ec);
					ig.Emit (OpCodes.Neg);
				}
				
				break;
				
			case Operator.LogicalNot:
				Expr.Emit (ec);
				ig.Emit (OpCodes.Ldc_I4_0);
				ig.Emit (OpCodes.Ceq);
				break;
				
			case Operator.OnesComplement:
				Expr.Emit (ec);
				ig.Emit (OpCodes.Not);
				break;
				
			case Operator.AddressOf:
				((IMemoryLocation)Expr).AddressOf (ec, AddressOp.LoadStore);
				break;
				
			default:
				throw new Exception ("This should not happen: Operator = "
						     + Oper.ToString ());
			}
		}

		public override void EmitBranchable (EmitContext ec, Label target, bool on_true)
		{
			if (Oper == Operator.LogicalNot)
				Expr.EmitBranchable (ec, target, !on_true);
			else
				base.EmitBranchable (ec, target, on_true);
		}

		public override string ToString ()
		{
			return "Unary (" + Oper + ", " + Expr + ")";
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Unary target = (Unary) t;

			target.Expr = Expr.Clone (clonectx);
		}
	}

	//
	// Unary operators are turned into Indirection expressions
	// after semantic analysis (this is so we can take the address
	// of an indirection).
	//
	public class Indirection : Expression, IMemoryLocation, IAssignMethod, IVariable {
		Expression expr;
		LocalTemporary temporary;
		bool prepared;
		
		public Indirection (Expression expr, Location l)
		{
			this.expr = expr;
			type = TypeManager.HasElementType (expr.Type) ? TypeManager.GetElementType (expr.Type) : expr.Type;
			eclass = ExprClass.Variable;
			loc = l;
		}
		
		public override void Emit (EmitContext ec)
		{
			if (!prepared)
				expr.Emit (ec);
			
			LoadFromPtr (ec.ig, Type);
		}

		public void Emit (EmitContext ec, bool leave_copy)
		{
			Emit (ec);
			if (leave_copy) {
				ec.ig.Emit (OpCodes.Dup);
				temporary = new LocalTemporary (expr.Type);
				temporary.Store (ec);
			}
		}
		
		public void EmitAssign (EmitContext ec, Expression source, bool leave_copy, bool prepare_for_load)
		{
			prepared = prepare_for_load;
			
			expr.Emit (ec);

			if (prepare_for_load)
				ec.ig.Emit (OpCodes.Dup);
			
			source.Emit (ec);
			if (leave_copy) {
				ec.ig.Emit (OpCodes.Dup);
				temporary = new LocalTemporary (expr.Type);
				temporary.Store (ec);
			}
			
			StoreFromPtr (ec.ig, type);
			
			if (temporary != null) {
				temporary.Emit (ec);
				temporary.Release (ec);
			}
		}
		
		public void AddressOf (EmitContext ec, AddressOp Mode)
		{
			expr.Emit (ec);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			return DoResolve (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			//
			// Born fully resolved
			//
			return this;
		}
		
		public override string ToString ()
		{
			return "*(" + expr + ")";
		}

		#region IVariable Members

		public VariableInfo VariableInfo {
			get { return null; }
		}

		public bool VerifyFixed ()
		{
			// A pointer-indirection is always fixed.
			return true;
		}

		#endregion
	}
	
	/// <summary>
	///   Unary Mutator expressions (pre and post ++ and --)
	/// </summary>
	///
	/// <remarks>
	///   UnaryMutator implements ++ and -- expressions.   It derives from
	///   ExpressionStatement becuase the pre/post increment/decrement
	///   operators can be used in a statement context.
	///
	/// FIXME: Idea, we could split this up in two classes, one simpler
	/// for the common case, and one with the extra fields for more complex
	/// classes (indexers require temporary access;  overloaded require method)
	///
	/// </remarks>
	public class UnaryMutator : ExpressionStatement {
		[Flags]
		public enum Mode : byte {
			IsIncrement    = 0,
			IsDecrement    = 1,
			IsPre          = 0,
			IsPost         = 2,
			
			PreIncrement   = 0,
			PreDecrement   = IsDecrement,
			PostIncrement  = IsPost,
			PostDecrement  = IsPost | IsDecrement
		}

		Mode mode;
		bool is_expr = false;
		bool recurse = false;

		Expression expr;

		//
		// This is expensive for the simplest case.
		//
		UserOperatorCall method;

		public UnaryMutator (Mode m, Expression e, Location l)
		{
			mode = m;
			loc = l;
			expr = e;
		}

		static string OperName (Mode mode)
		{
			return (mode == Mode.PreIncrement || mode == Mode.PostIncrement) ?
				"++" : "--";
		}

		/// <summary>
		///   Returns whether an object of type `t' can be incremented
		///   or decremented with add/sub (ie, basically whether we can
		///   use pre-post incr-decr operations on it, but it is not a
		///   System.Decimal, which we require operator overloading to catch)
		/// </summary>
		static bool IsIncrementableNumber (Type t)
		{
			return (t == TypeManager.sbyte_type) ||
				(t == TypeManager.byte_type) ||
				(t == TypeManager.short_type) ||
				(t == TypeManager.ushort_type) ||
				(t == TypeManager.int32_type) ||
				(t == TypeManager.uint32_type) ||
				(t == TypeManager.int64_type) ||
				(t == TypeManager.uint64_type) ||
				(t == TypeManager.char_type) ||
				(TypeManager.IsSubclassOf (t, TypeManager.enum_type)) ||
				(t == TypeManager.float_type) ||
				(t == TypeManager.double_type) ||
				(t.IsPointer && t != TypeManager.void_ptr_type);
		}

		Expression ResolveOperator (EmitContext ec)
		{
			Type expr_type = expr.Type;

			//
			// Step 1: Perform Operator Overload location
			//
			Expression mg;
			string op_name;
			
			if (mode == Mode.PreIncrement || mode == Mode.PostIncrement)
				op_name = "op_Increment";
			else 
				op_name = "op_Decrement";

			mg = MemberLookup (ec.ContainerType, expr_type, op_name, MemberTypes.Method, AllBindingFlags, loc);

			if (mg != null) {
				method = UserOperatorCall.MakeSimpleCall (
					ec, (MethodGroupExpr) mg, expr, loc);

				type = method.Type;
			} else if (!IsIncrementableNumber (expr_type)) {
				Error (187, "No such operator '" + OperName (mode) + "' defined for type '" +
				       TypeManager.CSharpName (expr_type) + "'");
				   return null;
			}

			//
			// The operand of the prefix/postfix increment decrement operators
			// should be an expression that is classified as a variable,
			// a property access or an indexer access
			//
			type = expr_type;
			if (expr.eclass == ExprClass.Variable){
				LocalVariableReference var = expr as LocalVariableReference;
				if ((var != null) && var.IsReadOnly) {
					Error (1604, "cannot assign to `" + var.Name + "' because it is readonly");
					return null;
				}
			} else if (expr.eclass == ExprClass.IndexerAccess || expr.eclass == ExprClass.PropertyAccess){
				expr = expr.ResolveLValue (ec, this, Location);
				if (expr == null)
					return null;
			} else {
				Report.Error (1059, loc, "The operand of an increment or decrement operator must be a variable, property or indexer");
				return null;
			}

			return this;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			expr = expr.Resolve (ec);
			
			if (expr == null)
				return null;

			eclass = ExprClass.Value;

#if GMCS_SOURCE
			if (TypeManager.IsNullableValueType (expr.Type))
				return new Nullable.LiftedUnaryMutator (mode, expr, loc).Resolve (ec);
#endif

			return ResolveOperator (ec);
		}

		//
		// Loads the proper "1" into the stack based on the type, then it emits the
		// opcode for the operation requested
		//
		void LoadOneAndEmitOp (EmitContext ec, Type t)
		{
			//
			// Measure if getting the typecode and using that is more/less efficient
			// that comparing types.  t.GetTypeCode() is an internal call.
			//
			ILGenerator ig = ec.ig;
						     
			if (t == TypeManager.uint64_type || t == TypeManager.int64_type)
				LongConstant.EmitLong (ig, 1);
			else if (t == TypeManager.double_type)
				ig.Emit (OpCodes.Ldc_R8, 1.0);
			else if (t == TypeManager.float_type)
				ig.Emit (OpCodes.Ldc_R4, 1.0F);
			else if (t.IsPointer){
				Type et = TypeManager.GetElementType (t);
				int n = GetTypeSize (et);
				
				if (n == 0)
					ig.Emit (OpCodes.Sizeof, et);
				else
					IntConstant.EmitInt (ig, n);
			} else 
				ig.Emit (OpCodes.Ldc_I4_1);

			//
			// Now emit the operation
			//
			if (ec.CheckState){
				if (t == TypeManager.int32_type ||
				    t == TypeManager.int64_type){
					if ((mode & Mode.IsDecrement) != 0)
						ig.Emit (OpCodes.Sub_Ovf);
					else
						ig.Emit (OpCodes.Add_Ovf);
				} else if (t == TypeManager.uint32_type ||
					   t == TypeManager.uint64_type){
					if ((mode & Mode.IsDecrement) != 0)
						ig.Emit (OpCodes.Sub_Ovf_Un);
					else
						ig.Emit (OpCodes.Add_Ovf_Un);
				} else {
					if ((mode & Mode.IsDecrement) != 0)
						ig.Emit (OpCodes.Sub_Ovf);
					else
						ig.Emit (OpCodes.Add_Ovf);
				}
			} else {
				if ((mode & Mode.IsDecrement) != 0)
					ig.Emit (OpCodes.Sub);
				else
					ig.Emit (OpCodes.Add);
			}

			if (t == TypeManager.sbyte_type){
				if (ec.CheckState)
					ig.Emit (OpCodes.Conv_Ovf_I1);
				else
					ig.Emit (OpCodes.Conv_I1);
			} else if (t == TypeManager.byte_type){
				if (ec.CheckState)
					ig.Emit (OpCodes.Conv_Ovf_U1);
				else
					ig.Emit (OpCodes.Conv_U1);
			} else if (t == TypeManager.short_type){
				if (ec.CheckState)
					ig.Emit (OpCodes.Conv_Ovf_I2);
				else
					ig.Emit (OpCodes.Conv_I2);
			} else if (t == TypeManager.ushort_type || t == TypeManager.char_type){
				if (ec.CheckState)
					ig.Emit (OpCodes.Conv_Ovf_U2);
				else
					ig.Emit (OpCodes.Conv_U2);
			}
			
		}

		void EmitCode (EmitContext ec, bool is_expr)
		{
			recurse = true;
			this.is_expr = is_expr;
			((IAssignMethod) expr).EmitAssign (ec, this, is_expr && (mode == Mode.PreIncrement || mode == Mode.PreDecrement), true);
		}

		public override void Emit (EmitContext ec)
		{
			//
			// We use recurse to allow ourselfs to be the source
			// of an assignment. This little hack prevents us from
			// having to allocate another expression
			//
			if (recurse) {
				((IAssignMethod) expr).Emit (ec, is_expr && (mode == Mode.PostIncrement || mode == Mode.PostDecrement));
				if (method == null)
					LoadOneAndEmitOp (ec, expr.Type);
				else
					ec.ig.Emit (OpCodes.Call, (MethodInfo)method.Method);
				recurse = false;
				return;
			}

			EmitCode (ec, true);
		}

		public override void EmitStatement (EmitContext ec)
		{
			EmitCode (ec, false);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			UnaryMutator target = (UnaryMutator) t;

			target.expr = expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   Base class for the `Is' and `As' classes. 
	/// </summary>
	///
	/// <remarks>
	///   FIXME: Split this in two, and we get to save the `Operator' Oper
	///   size. 
	/// </remarks>
	public abstract class Probe : Expression {
		public Expression ProbeType;
		protected Expression expr;
		protected TypeExpr probe_type_expr;
		
		public Probe (Expression expr, Expression probe_type, Location l)
		{
			ProbeType = probe_type;
			loc = l;
			this.expr = expr;
		}

		public Expression Expr {
			get {
				return expr;
			}
		}

		public override Expression DoResolve (EmitContext ec)
		{
			probe_type_expr = ProbeType.ResolveAsTypeTerminal (ec, false);
			if (probe_type_expr == null)
				return null;

			expr = expr.Resolve (ec);
			if (expr == null)
				return null;
			
			if (expr.Type.IsPointer || probe_type_expr.Type.IsPointer) {
				Report.Error (244, loc, "The `{0}' operator cannot be applied to an operand of pointer type",
					OperatorName);
				return null;
			}

			if (expr.Type == TypeManager.anonymous_method_type) {
				Report.Error (837, loc, "The `{0}' operator cannot be applied to a lambda expression or anonymous method",
					OperatorName);
				return null;
			}

			return this;
		}

		protected abstract string OperatorName { get; }

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Probe target = (Probe) t;

			target.expr = expr.Clone (clonectx);
			target.ProbeType = ProbeType.Clone (clonectx);
		}

	}

	/// <summary>
	///   Implementation of the `is' operator.
	/// </summary>
	public class Is : Probe {
		public Is (Expression expr, Expression probe_type, Location l)
			: base (expr, probe_type, l)
		{
		}
		
		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			expr.Emit (ec);
			ig.Emit (OpCodes.Isinst, probe_type_expr.Type);
			ig.Emit (OpCodes.Ldnull);
			ig.Emit (OpCodes.Cgt_Un);
		}

		public override void EmitBranchable (EmitContext ec, Label target, bool on_true)
		{
			ILGenerator ig = ec.ig;

			expr.Emit (ec);
			ig.Emit (OpCodes.Isinst, probe_type_expr.Type);
			ig.Emit (on_true ? OpCodes.Brtrue : OpCodes.Brfalse, target);
		}

		Expression CreateConstantResult (bool result)
		{
			if (result)
				Report.Warning (183, 1, loc, "The given expression is always of the provided (`{0}') type",
					TypeManager.CSharpName (probe_type_expr.Type));
			else
				Report.Warning (184, 1, loc, "The given expression is never of the provided (`{0}') type",
					TypeManager.CSharpName (probe_type_expr.Type));

			return new BoolConstant (result, loc);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (base.DoResolve (ec) == null)
				return null;

			Type d = expr.Type;
			bool d_is_nullable = false;

			if (expr is Constant) {
				//
				// If E is a method group or the null literal, of if the type of E is a reference
				// type or a nullable type and the value of E is null, the result is false
				//
				if (expr.IsNull)
					return CreateConstantResult (false);
			} else if (TypeManager.IsNullableType (d) && !TypeManager.ContainsGenericParameters (d)) {
				d = TypeManager.GetTypeArguments (d) [0];
				d_is_nullable = true;
			}

			type = TypeManager.bool_type;
			eclass = ExprClass.Value;
			Type t = probe_type_expr.Type;
			bool t_is_nullable = false;
			if (TypeManager.IsNullableType (t) && !TypeManager.ContainsGenericParameters (t)) {
				t = TypeManager.GetTypeArguments (t) [0];
				t_is_nullable = true;
			}

			if (t.IsValueType) {
				if (d == t) {
					//
					// D and T are the same value types but D can be null
					//
					if (d_is_nullable && !t_is_nullable)
						return Nullable.HasValue.Create (expr, ec);
					
					//
					// The result is true if D and T are the same value types
					//
					return CreateConstantResult (true);
				}

				if (TypeManager.IsGenericParameter (d))
					return ResolveGenericParameter (t, d);

				//
				// An unboxing conversion exists
				//
				if (Convert.ExplicitReferenceConversionExists (d, t))
					return this;
			} else {
				if (TypeManager.IsGenericParameter (t))
					return ResolveGenericParameter (d, t);

				if (d.IsValueType) {
					bool temp;
					if (Convert.ImplicitBoxingConversionExists (expr, t, out temp))
						return CreateConstantResult (true);
				} else {
					if (TypeManager.IsGenericParameter (d))
						return ResolveGenericParameter (t, d);

					if (TypeManager.ContainsGenericParameters (d))
						return this;

					if (Convert.ImplicitReferenceConversionExists (expr, t) ||
						Convert.ExplicitReferenceConversionExists (d, t)) {
						return this;
					}
				}
			}

			return CreateConstantResult (false);
		}

		Expression ResolveGenericParameter (Type d, Type t)
		{
#if GMCS_SOURCE
			GenericConstraints constraints = TypeManager.GetTypeParameterConstraints (t);
			if (constraints != null) {
				if (constraints.IsReferenceType && d.IsValueType)
					return CreateConstantResult (false);

				if (constraints.IsValueType && !d.IsValueType)
					return CreateConstantResult (false);
			}

			expr = new BoxedCast (expr, d);
			return this;
#else
			return null;
#endif
		}
		
		protected override string OperatorName {
			get { return "is"; }
		}
	}

	/// <summary>
	///   Implementation of the `as' operator.
	/// </summary>
	public class As : Probe {
		public As (Expression expr, Expression probe_type, Location l)
			: base (expr, probe_type, l)
		{
		}

		bool do_isinst = false;
		Expression resolved_type;
		
		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			expr.Emit (ec);

			if (do_isinst)
				ig.Emit (OpCodes.Isinst, probe_type_expr.Type);

#if GMCS_SOURCE
			if (TypeManager.IsNullableType (type))
				ig.Emit (OpCodes.Unbox_Any, type);
#endif
		}

		static void Error_CannotConvertType (Type source, Type target, Location loc)
		{
			Report.Error (39, loc, "Cannot convert type `{0}' to `{1}' via a built-in conversion",
				TypeManager.CSharpName (source),
				TypeManager.CSharpName (target));
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			if (resolved_type == null) {
				resolved_type = base.DoResolve (ec);

				if (resolved_type == null)
					return null;
			}

			type = probe_type_expr.Type;
			eclass = ExprClass.Value;
			Type etype = expr.Type;

			if (type.IsValueType && !TypeManager.IsNullableType (type)) {
				Report.Error (77, loc, "The `as' operator cannot be used with a non-nullable value type `{0}'",
					      TypeManager.CSharpName (type));
				return null;
			
			}

#if GMCS_SOURCE
			//
			// If the type is a type parameter, ensure
			// that it is constrained by a class
			//
			TypeParameterExpr tpe = probe_type_expr as TypeParameterExpr;
			if (tpe != null){
				GenericConstraints constraints = tpe.TypeParameter.GenericConstraints;
				bool error = false;
				
				if (constraints == null)
					error = true;
				else {
					if (!constraints.HasClassConstraint)
						if ((constraints.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) == 0)
							error = true;
				}
				if (error){
					Report.Error (413, loc,
						      "The as operator requires that the `{0}' type parameter be constrained by a class",
						      probe_type_expr.GetSignatureForError ());
					return null;
				}
			}
#endif
			if (expr.IsNull && TypeManager.IsNullableType (type)) {
				Report.Warning (458, 2, loc, "The result of the expression is always `null' of type `{0}'",
					TypeManager.CSharpName (type));
			}
			
			Expression e = Convert.ImplicitConversion (ec, expr, type, loc);
			if (e != null){
				expr = e;
				do_isinst = false;
				return this;
			}

			if (Convert.ExplicitReferenceConversionExists (etype, type)){
				if (TypeManager.IsGenericParameter (etype))
					expr = new BoxedCast (expr, etype);

				do_isinst = true;
				return this;
			}

			if (TypeManager.ContainsGenericParameters (etype) ||
			    TypeManager.ContainsGenericParameters (type)) {
				expr = new BoxedCast (expr, etype);
				do_isinst = true;
				return this;
			}

			Error_CannotConvertType (etype, type, loc);
			return null;
		}

		protected override string OperatorName {
			get { return "as"; }
		}
	
		public override bool GetAttributableValue (Type value_type, out object value)
		{
			return expr.GetAttributableValue (value_type, out value);
		}
	}
	
	/// <summary>
	///   This represents a typecast in the source language.
	///
	///   FIXME: Cast expressions have an unusual set of parsing
	///   rules, we need to figure those out.
	/// </summary>
	public class Cast : Expression {
		Expression target_type;
		Expression expr;
			
		public Cast (Expression cast_type, Expression expr)
			: this (cast_type, expr, cast_type.Location)
		{
		}

		public Cast (Expression cast_type, Expression expr, Location loc)
		{
			this.target_type = cast_type;
			this.expr = expr;
			this.loc = loc;

			if (target_type == TypeManager.system_void_expr)
				Error_VoidInvalidInTheContext (loc);
		}

		public Expression TargetType {
			get { return target_type; }
		}

		public Expression Expr {
			get { return expr; }
		}

		public override Expression DoResolve (EmitContext ec)
		{
			expr = expr.Resolve (ec);
			if (expr == null)
				return null;

			TypeExpr target = target_type.ResolveAsTypeTerminal (ec, false);
			if (target == null)
				return null;

			type = target.Type;

			if (type.IsAbstract && type.IsSealed) {
				Report.Error (716, loc, "Cannot convert to static type `{0}'", TypeManager.CSharpName (type));
				return null;
			}

			eclass = ExprClass.Value;

			Constant c = expr as Constant;
			if (c != null) {
				c = c.TryReduce (ec, type, loc);
				if (c != null)
					return c;
			}

			if (type.IsPointer && !ec.InUnsafe) {
				UnsafeError (loc);
				return null;
			}
			expr = Convert.ExplicitConversion (ec, expr, type, loc);
			return expr;
		}
		
		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Should not happen");
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Cast target = (Cast) t;

			target.target_type = target_type.Clone (clonectx);
			target.expr = expr.Clone (clonectx);
		}
	}
	
	//
	// C# 2.0 Default value expression
	//
	public class DefaultValueExpression : Expression
	{
		Expression expr;

		public DefaultValueExpression (Expression expr, Location loc)
		{
			this.expr = expr;
			this.loc = loc;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			TypeExpr texpr = expr.ResolveAsTypeTerminal (ec, false);
			if (texpr == null)
				return null;

			type = texpr.Type;

			if (type == TypeManager.void_type) {
				Error_VoidInvalidInTheContext (loc);
				return null;
			}

			if (TypeManager.IsGenericParameter (type))
			{
				GenericConstraints constraints = TypeManager.GetTypeParameterConstraints(type);
				if (constraints != null && constraints.IsReferenceType)
					return new NullDefault (new NullLiteral (Location), type);
			}
			else
			{
				Constant c = New.Constantify(type);
				if (c != null)
					return new NullDefault (c, type);

				if (!TypeManager.IsValueType (type))
					return new NullDefault (new NullLiteral (Location), type);
			}
			eclass = ExprClass.Variable;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			LocalTemporary temp_storage = new LocalTemporary(type);

			temp_storage.AddressOf(ec, AddressOp.LoadStore);
			ec.ig.Emit(OpCodes.Initobj, type);
			temp_storage.Emit(ec);
		}
		
		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			DefaultValueExpression target = (DefaultValueExpression) t;
			
			target.expr = expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   Binary operators
	/// </summary>
	public class Binary : Expression {
		public enum Operator : byte {
			Multiply, Division, Modulus,
			Addition, Subtraction,
			LeftShift, RightShift,
			LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual, 
			Equality, Inequality,
			BitwiseAnd,
			ExclusiveOr,
			BitwiseOr,
			LogicalAnd,
			LogicalOr,
			TOP
		}

		readonly Operator oper;
		protected Expression left, right;
		readonly bool is_compound;

		// This must be kept in sync with Operator!!!
		public static readonly string [] oper_names;
		
		static Binary ()
		{
			oper_names = new string [(int) Operator.TOP];

			oper_names [(int) Operator.Multiply] = "op_Multiply";
			oper_names [(int) Operator.Division] = "op_Division";
			oper_names [(int) Operator.Modulus] = "op_Modulus";
			oper_names [(int) Operator.Addition] = "op_Addition";
			oper_names [(int) Operator.Subtraction] = "op_Subtraction";
			oper_names [(int) Operator.LeftShift] = "op_LeftShift";
			oper_names [(int) Operator.RightShift] = "op_RightShift";
			oper_names [(int) Operator.LessThan] = "op_LessThan";
			oper_names [(int) Operator.GreaterThan] = "op_GreaterThan";
			oper_names [(int) Operator.LessThanOrEqual] = "op_LessThanOrEqual";
			oper_names [(int) Operator.GreaterThanOrEqual] = "op_GreaterThanOrEqual";
			oper_names [(int) Operator.Equality] = "op_Equality";
			oper_names [(int) Operator.Inequality] = "op_Inequality";
			oper_names [(int) Operator.BitwiseAnd] = "op_BitwiseAnd";
			oper_names [(int) Operator.BitwiseOr] = "op_BitwiseOr";
			oper_names [(int) Operator.ExclusiveOr] = "op_ExclusiveOr";
			oper_names [(int) Operator.LogicalOr] = "op_LogicalOr";
			oper_names [(int) Operator.LogicalAnd] = "op_LogicalAnd";
		}

		public Binary (Operator oper, Expression left, Expression right, bool isCompound)
			: this (oper, left, right)
		{
			this.is_compound = isCompound;
		}

		public Binary (Operator oper, Expression left, Expression right)
		{
			this.oper = oper;
			this.left = left;
			this.right = right;
			this.loc = left.Location;
		}

		public Operator Oper {
			get {
				return oper;
			}
		}
		
		/// <summary>
		///   Returns a stringified representation of the Operator
		/// </summary>
		string OperName (Operator oper)
		{
			string s;
			switch (oper){
			case Operator.Multiply:
				s = "*";
				break;
			case Operator.Division:
				s = "/";
				break;
			case Operator.Modulus:
				s = "%";
				break;
			case Operator.Addition:
				s = "+";
				break;
			case Operator.Subtraction:
				s = "-";
				break;
			case Operator.LeftShift:
				s = "<<";
				break;
			case Operator.RightShift:
				s = ">>";
				break;
			case Operator.LessThan:
				s = "<";
				break;
			case Operator.GreaterThan:
				s = ">";
				break;
			case Operator.LessThanOrEqual:
				s = "<=";
				break;
			case Operator.GreaterThanOrEqual:
				s = ">=";
				break;
			case Operator.Equality:
				s = "==";
				break;
			case Operator.Inequality:
				s = "!=";
				break;
			case Operator.BitwiseAnd:
				s = "&";
				break;
			case Operator.BitwiseOr:
				s = "|";
				break;
			case Operator.ExclusiveOr:
				s = "^";
				break;
			case Operator.LogicalOr:
				s = "||";
				break;
			case Operator.LogicalAnd:
				s = "&&";
				break;
			default:
				s = oper.ToString ();
				break;
			}

			if (is_compound)
				return s + "=";

			return s;
		}

		public override string ToString ()
		{
			return "operator " + OperName (oper) + "(" + left.ToString () + ", " +
				right.ToString () + ")";
		}
		
		Expression ForceConversion (EmitContext ec, Expression expr, Type target_type)
		{
			if (expr.Type == target_type)
				return expr;

			return Convert.ImplicitConversion (ec, expr, target_type, loc);
		}

		void Error_OperatorAmbiguous (Location loc, Operator oper, Type l, Type r)
		{
			Report.Error (
				34, loc, "Operator `" + OperName (oper) 
				+ "' is ambiguous on operands of type `"
				+ TypeManager.CSharpName (l) + "' "
				+ "and `" + TypeManager.CSharpName (r)
				+ "'");
		}

		bool IsConvertible (EmitContext ec, Expression le, Expression re, Type t)
		{
			return Convert.ImplicitConversionExists (ec, le, t) && Convert.ImplicitConversionExists (ec, re, t);
		}

		bool VerifyApplicable_Predefined (EmitContext ec, Type t)
		{
			if (!IsConvertible (ec, left, right, t))
				return false;
			left = ForceConversion (ec, left, t);
			right = ForceConversion (ec, right, t);
			type = t;
			return true;
		}

		bool IsApplicable_String (EmitContext ec, Expression le, Expression re, Operator oper)
		{
			bool l = Convert.ImplicitConversionExists (ec, le, TypeManager.string_type);
			bool r = Convert.ImplicitConversionExists (ec, re, TypeManager.string_type);

			if (oper == Operator.Equality || oper == Operator.Inequality)
				return l && r;
			if (oper == Operator.Addition)
				return l || r;
			return false;
		}

		bool OverloadResolve_PredefinedString (EmitContext ec, Operator oper)
		{
			if (!IsApplicable_String (ec, left, right, oper))
				return false;
			
			Type l = left.Type;
			Type r = right.Type;
			if (OverloadResolve_PredefinedIntegral (ec) ||
				OverloadResolve_PredefinedFloating (ec)) {
				Error_OperatorAmbiguous (loc, oper, l, r);
			}
			
			Type t = TypeManager.string_type;
			if (Convert.ImplicitConversionExists (ec, left, t))
				left = ForceConversion (ec, left, t);
			if (Convert.ImplicitConversionExists (ec, right, t))
				right = ForceConversion (ec, right, t);
			type = t;
			return true;
		}

		bool OverloadResolve_PredefinedIntegral (EmitContext ec)
		{
			return VerifyApplicable_Predefined (ec, TypeManager.int32_type) ||
				VerifyApplicable_Predefined (ec, TypeManager.uint32_type) ||
				VerifyApplicable_Predefined (ec, TypeManager.int64_type) ||
				VerifyApplicable_Predefined (ec, TypeManager.uint64_type) ||
				false;
		}

		bool OverloadResolve_PredefinedFloating (EmitContext ec)
		{
			return VerifyApplicable_Predefined (ec, TypeManager.float_type) ||
				VerifyApplicable_Predefined (ec, TypeManager.double_type) ||
				false;
		}

		static public void Error_OperatorCannotBeApplied (Location loc, string name, Type l, Type r)
		{
			Error_OperatorCannotBeApplied (loc, name, TypeManager.CSharpName (l), TypeManager.CSharpName (r));
		}

		public static void Error_OperatorCannotBeApplied (Location loc, string name, string left, string right)
		{
			Report.Error (19, loc, "Operator `{0}' cannot be applied to operands of type `{1}' and `{2}'",
				name, left, right);
		}
		
		protected void Error_OperatorCannotBeApplied ()
		{
			Error_OperatorCannotBeApplied (Location, OperName (oper), TypeManager.CSharpName (left.Type),
				TypeManager.CSharpName(right.Type));
		}

		static bool IsUnsigned (Type t)
		{
			while (t.IsPointer)
				t = t.GetElementType ();

			return (t == TypeManager.uint32_type || t == TypeManager.uint64_type ||
				t == TypeManager.ushort_type || t == TypeManager.byte_type);
		}

		Expression Make32or64 (EmitContext ec, Expression e)
		{
			Type t= e.Type;
			
			if (t == TypeManager.int32_type || t == TypeManager.uint32_type ||
			    t == TypeManager.int64_type || t == TypeManager.uint64_type)
				return e;
			Expression ee = Convert.ImplicitConversion (ec, e, TypeManager.int32_type, loc);
			if (ee != null)
				return ee;
			ee = Convert.ImplicitConversion (ec, e, TypeManager.uint32_type, loc);
			if (ee != null)
				return ee;
			ee = Convert.ImplicitConversion (ec, e, TypeManager.int64_type, loc);
			if (ee != null)
				return ee;
			ee = Convert.ImplicitConversion (ec, e, TypeManager.uint64_type, loc);
			if (ee != null)
				return ee;
			return null;
		}
					
		Expression CheckShiftArguments (EmitContext ec)
		{
			Expression new_left = Make32or64 (ec, left);
			Expression new_right = ForceConversion (ec, right, TypeManager.int32_type);
			if (new_left == null || new_right == null) {
				Error_OperatorCannotBeApplied ();
				return null;
			}
			type = new_left.Type;
			int shiftmask = (type == TypeManager.int32_type || type == TypeManager.uint32_type) ? 31 : 63;
			left = new_left;
			right = new Binary (Binary.Operator.BitwiseAnd, new_right, new IntConstant (shiftmask, loc)).DoResolve (ec);
			return this;
		}

		//
		// This is used to check if a test 'x == null' can be optimized to a reference equals,
		// i.e., not invoke op_Equality.
		//
		static bool EqualsNullIsReferenceEquals (Type t)
		{
			return t == TypeManager.object_type || t == TypeManager.string_type ||
				t == TypeManager.delegate_type || TypeManager.IsDelegateType (t);
		}

		static void Warning_UnintendedReferenceComparison (Location loc, string side, Type type)
		{
			Report.Warning ((side == "left" ? 252 : 253), 2, loc,
				"Possible unintended reference comparison; to get a value comparison, " +
				"cast the {0} hand side to type `{1}'.", side, TypeManager.CSharpName (type));
		}

		static void Warning_Constant_Result (Location loc, bool result, Type type)
		{
			Report.Warning (472, 2, loc, "The result of comparing `{0}' against null is always `{1}'. " +
					"This operation is undocumented and it is temporary supported for compatibility reasons only",
					TypeManager.CSharpName (type), result ? "true" : "false"); 
		}
			
		Expression ResolveOperator (EmitContext ec)
		{
			Type l = left.Type;
			Type r = right.Type;

			if (oper == Operator.Equality || oper == Operator.Inequality){
				if (right.Type == TypeManager.null_type){
					if (TypeManager.IsGenericParameter (l)){
						if (l.BaseType == TypeManager.value_type) {
							Error_OperatorCannotBeApplied ();
							return null;
						}
						
						left = new BoxedCast (left, TypeManager.object_type);
						Type = TypeManager.bool_type;
						return this;
					} 

					//
					// 7.9.9 Equality operators and null
					//
					// CSC 2 has this behavior, it allows structs to be compared
					// with the null literal *outside* of a generics context and
					// inlines that as true or false.
					//
					// This is, in my opinion, completely wrong.
					//
					if (RootContext.Version != LanguageVersion.ISO_1 && l.IsValueType) {
						if (!TypeManager.IsPrimitiveType (l) && !TypeManager.IsEnumType (l)) {
							if (MemberLookup (ec.ContainerType, l, oper_names [(int)Operator.Equality], MemberTypes.Method, AllBindingFlags, loc) == null &&
								MemberLookup (ec.ContainerType, l, oper_names [(int)Operator.Inequality], MemberTypes.Method, AllBindingFlags, loc) == null) {
								Error_OperatorCannotBeApplied ();
								return null;
							}
						}

						Warning_Constant_Result (loc, oper == Operator.Inequality, l);
						return new BoolConstant (oper == Operator.Inequality, loc);
					}
				}

				if (left is NullLiteral){
					if (TypeManager.IsGenericParameter (r)){
						if (r.BaseType == TypeManager.value_type) {
							Error_OperatorCannotBeApplied ();
							return null;
						}
						
						right = new BoxedCast (right, TypeManager.object_type);
						Type = TypeManager.bool_type;
						return this;
					}

					//
					// 7.9.9 Equality operators and null
					//
					// CSC 2 has this behavior, it allows structs to be compared
					// with the null literal *outside* of a generics context and
					// inlines that as true or false.
					//
					// This is, in my opinion, completely wrong.
					//
					if (RootContext.Version != LanguageVersion.ISO_1 && r.IsValueType){
						if (!TypeManager.IsPrimitiveType (r) && !TypeManager.IsEnumType (r)) {
							if (MemberLookup (ec.ContainerType, r, oper_names [(int) Operator.Equality], MemberTypes.Method, AllBindingFlags, loc) == null &&
								MemberLookup (ec.ContainerType, r, oper_names [(int) Operator.Inequality], MemberTypes.Method, AllBindingFlags, loc) == null) {
								Error_OperatorCannotBeApplied ();
								return null;
							}
						}

						Warning_Constant_Result (loc, oper == Operator.Inequality, r);
						return new BoolConstant (oper == Operator.Inequality, loc);
					}
				}

				//
				// Optimize out call to op_Equality in a few cases.
				//
				if ((l == TypeManager.null_type && EqualsNullIsReferenceEquals (r)) ||
				    (r == TypeManager.null_type && EqualsNullIsReferenceEquals (l))) {
					Type = TypeManager.bool_type;
					return this;
				}

				// IntPtr equality
				if (l == TypeManager.intptr_type && r == TypeManager.intptr_type) {
					Type = TypeManager.bool_type;
					return this;
				}

#if GMCS_SOURCE												
				//
				// Delegate equality
				//
				MethodGroupExpr mg = null;
				Type delegate_type = null;
				if (left.eclass == ExprClass.MethodGroup) {
					if (!TypeManager.IsDelegateType(r)) {
						Error_OperatorCannotBeApplied(Location, OperName(oper),
						        left.ExprClassName, right.ExprClassName);
						return null;
					}
					mg = (MethodGroupExpr)left;
					delegate_type = r;
				} else if (right.eclass == ExprClass.MethodGroup) {
					if (!TypeManager.IsDelegateType(l)) {
						Error_OperatorCannotBeApplied(Location, OperName(oper),
						        left.ExprClassName, right.ExprClassName);
						return null;
					}
					mg = (MethodGroupExpr)right;
					delegate_type = l;
				}

				if (mg != null) {
					Expression e = ImplicitDelegateCreation.Create (ec, mg, delegate_type, loc);
					if (e == null)
						return null;

					// Find operator method
					string op = oper_names[(int)oper];
					MemberInfo[] mi = TypeManager.MemberLookup(ec.ContainerType, null,
						TypeManager.delegate_type, MemberTypes.Method, AllBindingFlags, op, null);

					ArrayList args = new ArrayList(2);
					args.Add(new Argument(e, Argument.AType.Expression));
					if (delegate_type == l)
						args.Insert(0, new Argument(left, Argument.AType.Expression));
					else
						args.Add(new Argument(right, Argument.AType.Expression));

					return new BinaryMethod (TypeManager.bool_type, (MethodInfo)mi [0], args);
				}
#endif				
				if (l == TypeManager.anonymous_method_type || r == TypeManager.anonymous_method_type) {
					Error_OperatorCannotBeApplied(Location, OperName(oper),
						left.ExprClassName, right.ExprClassName);
					return null;
				}				
			}


			//
			// Do not perform operator overload resolution when both sides are
			// built-in types
			//
			MethodGroupExpr left_operators = null, right_operators = null;
			if (!(TypeManager.IsPrimitiveType (l) && TypeManager.IsPrimitiveType (r))) {
				//
				// Step 1: Perform Operator Overload location
				//
				string op = oper_names [(int) oper];

				MethodGroupExpr union;
				left_operators = MemberLookup (ec.ContainerType, l, op, MemberTypes.Method, AllBindingFlags, loc) as MethodGroupExpr;
				if (r != l){
					right_operators = MemberLookup (
						ec.ContainerType, r, op, MemberTypes.Method, AllBindingFlags, loc) as MethodGroupExpr;
					union = MethodGroupExpr.MakeUnionSet (left_operators, right_operators, loc);
				} else
					union = left_operators;

				if (union != null) {
					ArrayList args = new ArrayList (2);
					args.Add (new Argument (left));
					args.Add (new Argument (right));

					union = union.OverloadResolve (ec, ref args, true, loc);
					if (union != null)
						return new UserOperatorCall (union, args, CreateExpressionTree, loc);				
				}
			}

			//
			// String concatenation
			// 
			// string operator + (string x, string y);
			// string operator + (string x, object y);
			// string operator + (object x, string y);
			//
			if (oper == Operator.Addition && !TypeManager.IsDelegateType (l)) {
				// 
				// Either left or right expression is implicitly convertible to string
				//
				if (OverloadResolve_PredefinedString (ec, oper)) {
					if (r == TypeManager.void_type || l == TypeManager.void_type) {
						Error_OperatorCannotBeApplied ();
						return null;
					}

					//
					// Constants folding for strings and nulls
					//
					if (left.Type == TypeManager.string_type && right.Type == TypeManager.string_type &&
						left is Constant && right is Constant) {
						string lvalue = (string)((Constant) left).GetValue ();
						string rvalue = (string)((Constant) right).GetValue ();
						return new StringConstant (lvalue + rvalue, left.Location);
					}

					// 
					// Append to existing string concatenation
					//
					if (left is StringConcat) {
						((StringConcat) left).Append (ec, right);
						return left;
					}

					//
					// Otherwise, start a new concat expression using converted expression
					//
					return new StringConcat (ec, loc, left, right).Resolve (ec);
				}

				//
				// Transform a + ( - b) into a - b
				//
				if (right is Unary){
					Unary right_unary = (Unary) right;

					if (right_unary.Oper == Unary.Operator.UnaryNegation){
						return new Binary (Operator.Subtraction, left, right_unary.Expr).Resolve (ec);
					}
				}
			}

			if (oper == Operator.Equality || oper == Operator.Inequality){
				if (l == TypeManager.bool_type || r == TypeManager.bool_type){
					if (r != TypeManager.bool_type || l != TypeManager.bool_type){
						Error_OperatorCannotBeApplied ();
						return null;
					}

					type = TypeManager.bool_type;
					return this;
				}

				if (l.IsPointer || r.IsPointer) {
					if (l.IsPointer && r.IsPointer) {
						type = TypeManager.bool_type;
						return this;
					}

					if (l.IsPointer && r == TypeManager.null_type) {
						right = new EmptyConstantCast (NullPointer.Null, l);
						type = TypeManager.bool_type;
						return this;
					}

					if (r.IsPointer && l == TypeManager.null_type) {
						left = new EmptyConstantCast (NullPointer.Null, r);
						type = TypeManager.bool_type;
						return this;
					}
				}

#if GMCS_SOURCE
				if (l.IsGenericParameter && r.IsGenericParameter) {
					GenericConstraints l_gc, r_gc;

					l_gc = TypeManager.GetTypeParameterConstraints (l);
					r_gc = TypeManager.GetTypeParameterConstraints (r);

					if ((l_gc == null) || (r_gc == null) ||
					    !(l_gc.HasReferenceTypeConstraint || l_gc.HasClassConstraint) ||
					    !(r_gc.HasReferenceTypeConstraint || r_gc.HasClassConstraint)) {
						Error_OperatorCannotBeApplied ();
						return null;
					}

				}
#endif

				//
				// operator != (object a, object b)
				// operator == (object a, object b)
				//
				// For this to be used, both arguments have to be reference-types.
				// Read the rationale on the spec (14.9.6)
				//
				if (!(l.IsValueType || r.IsValueType)){
					type = TypeManager.bool_type;

					if (l == r)
						return this;
					
					//
					// Also, a standard conversion must exist from either one
					//
					// NOTE: An interface is converted to the object before the
					// standard conversion is applied. It's not clear from the
					// standard but it looks like it works like that.
					//
					if (l.IsInterface)
						l = TypeManager.object_type;
					if (r.IsInterface)
						r = TypeManager.object_type;					
					
					bool left_to_right =
						Convert.ImplicitStandardConversionExists (left, r);
					bool right_to_left = !left_to_right &&
						Convert.ImplicitStandardConversionExists (right, l);

					if (!left_to_right && !right_to_left) {
						Error_OperatorCannotBeApplied ();
						return null;
					}

					if (left_to_right && left_operators != null &&
					    Report.WarningLevel >= 2) {
						ArrayList args = new ArrayList (2);
						args.Add (new Argument (left, Argument.AType.Expression));
						args.Add (new Argument (left, Argument.AType.Expression));
						if (left_operators.OverloadResolve (ec, ref args, true, Location.Null) != null)
							Warning_UnintendedReferenceComparison (loc, "right", l);
					}

					if (right_to_left && right_operators != null &&
					    Report.WarningLevel >= 2) {
						ArrayList args = new ArrayList (2);
						args.Add (new Argument (right, Argument.AType.Expression));
						args.Add (new Argument (right, Argument.AType.Expression));
						if (right_operators.OverloadResolve (ec, ref args, true, Location.Null) != null)
							Warning_UnintendedReferenceComparison (loc, "left", r);
					}

					//
					// We are going to have to convert to an object to compare
					//
					if (l != TypeManager.object_type)
						left = EmptyCast.Create (left, TypeManager.object_type);
					if (r != TypeManager.object_type)
						right = EmptyCast.Create (right, TypeManager.object_type);

					return this;
				}
			}

			// Only perform numeric promotions on:
			// +, -, *, /, %, &, |, ^, ==, !=, <, >, <=, >=
			//
			if (oper == Operator.Addition || oper == Operator.Subtraction) {
				if (TypeManager.IsDelegateType (l)){
					if (((right.eclass == ExprClass.MethodGroup) ||
					     (r == TypeManager.anonymous_method_type))){
						if ((RootContext.Version != LanguageVersion.ISO_1)){
							Expression tmp = Convert.ImplicitConversionRequired (ec, right, l, loc);
							if (tmp == null)
								return null;
							right = tmp;
							r = right.Type;
						}
					}

					if (TypeManager.IsDelegateType (r) || right is NullLiteral){
						MethodInfo method;
						ArrayList args = new ArrayList (2);
					
						args = new ArrayList (2);
						args.Add (new Argument (left, Argument.AType.Expression));
						args.Add (new Argument (right, Argument.AType.Expression));
					
						if (oper == Operator.Addition)
							method = TypeManager.delegate_combine_delegate_delegate;
						else
							method = TypeManager.delegate_remove_delegate_delegate;

						if (!TypeManager.IsEqual (l, r) && !(right is NullLiteral)) {
							Error_OperatorCannotBeApplied ();
							return null;
						}

						return new BinaryDelegate (l, method, args);
					}
				}

				//
				// Pointer arithmetic:
				//
				// T* operator + (T* x, int y);
				// T* operator + (T* x, uint y);
				// T* operator + (T* x, long y);
				// T* operator + (T* x, ulong y);
				//
				// T* operator + (int y,   T* x);
				// T* operator + (uint y,  T *x);
				// T* operator + (long y,  T *x);
				// T* operator + (ulong y, T *x);
				//
				// T* operator - (T* x, int y);
				// T* operator - (T* x, uint y);
				// T* operator - (T* x, long y);
				// T* operator - (T* x, ulong y);
				//
				// long operator - (T* x, T *y)
				//
				if (l.IsPointer){
					if (r.IsPointer && oper == Operator.Subtraction){
						if (r == l)
							return new PointerArithmetic (
								false, left, right, TypeManager.int64_type,
								loc).Resolve (ec);
					} else {
						Expression t = Make32or64 (ec, right);
						if (t != null)
							return new PointerArithmetic (oper == Operator.Addition, left, t, l, loc).Resolve (ec);
					}
				} else if (r.IsPointer && oper == Operator.Addition){
					Expression t = Make32or64 (ec, left);
					if (t != null)
						return new PointerArithmetic (true, right, t, r, loc).Resolve (ec);
				}
			}
			
			//
			// Enumeration operators
			//
			bool lie = TypeManager.IsEnumType (l);
			bool rie = TypeManager.IsEnumType (r);
			if (lie || rie){
				Expression temp;

				// U operator - (E e, E f)
				if (lie && rie){
					if (oper == Operator.Subtraction){
						if (l == r){
							type = TypeManager.EnumToUnderlying (l);
							return this;
						}
						Error_OperatorCannotBeApplied ();
						return null;
					}
				}
					
				//
				// operator + (E e, U x)
				// operator - (E e, U x)
				//
				if (oper == Operator.Addition || oper == Operator.Subtraction){
					Type enum_type = lie ? l : r;
					Type other_type = lie ? r : l;
					Type underlying_type = TypeManager.EnumToUnderlying (enum_type);
					
					if (underlying_type != other_type){
						temp = Convert.ImplicitConversion (ec, lie ? right : left, underlying_type, loc);
						if (temp != null){
							if (lie)
								right = temp;
							else
								left = temp;
							type = enum_type;
							return this;
						}
							
						Error_OperatorCannotBeApplied ();
						return null;
					}

					type = enum_type;
					return this;
				}
				
				if (!rie){
					temp = Convert.ImplicitConversion (ec, right, l, loc);
					if (temp != null)
						right = temp;
					else {
						Error_OperatorCannotBeApplied ();
						return null;
					}
				} if (!lie){
					temp = Convert.ImplicitConversion (ec, left, r, loc);
					if (temp != null){
						left = temp;
						l = r;
					} else {
						Error_OperatorCannotBeApplied ();
						return null;
					}
				}

				if (oper == Operator.Equality || oper == Operator.Inequality ||
				    oper == Operator.LessThanOrEqual || oper == Operator.LessThan ||
				    oper == Operator.GreaterThanOrEqual || oper == Operator.GreaterThan){
					if (left.Type != right.Type){
						Error_OperatorCannotBeApplied ();
						return null;
					}
					type = TypeManager.bool_type;
					return this;
				}

				if (oper == Operator.BitwiseAnd ||
				    oper == Operator.BitwiseOr ||
				    oper == Operator.ExclusiveOr){
					if (left.Type != right.Type){
						Error_OperatorCannotBeApplied ();
						return null;
					}
					type = l;
					return this;
				}
				Error_OperatorCannotBeApplied ();
				return null;
			}
			
			if (oper == Operator.LeftShift || oper == Operator.RightShift)
				return CheckShiftArguments (ec);

			if (oper == Operator.LogicalOr || oper == Operator.LogicalAnd){
				if (l == TypeManager.bool_type && r == TypeManager.bool_type) {
					type = TypeManager.bool_type;
					return this;
				}

				Expression left_operators_e = l == TypeManager.bool_type ?
					left : Convert.ImplicitUserConversion (ec, left, TypeManager.bool_type, loc);
				Expression right_operators_e = r == TypeManager.bool_type ?
					right : Convert.ImplicitUserConversion (ec, right, TypeManager.bool_type, loc);

				if (left_operators_e != null && right_operators_e != null) {
					left = left_operators_e;
					right = right_operators_e;
					type = TypeManager.bool_type;
					return this;
				}

				Expression e = new ConditionalLogicalOperator (
					oper == Operator.LogicalAnd, left, right, l, loc);
				return e.Resolve (ec);
			} 

			Expression orig_left = left;
			Expression orig_right = right;

			//
			// operator & (bool x, bool y)
			// operator | (bool x, bool y)
			// operator ^ (bool x, bool y)
			//
			if (oper == Operator.BitwiseAnd ||
			    oper == Operator.BitwiseOr ||
			    oper == Operator.ExclusiveOr) {
				if (OverloadResolve_PredefinedIntegral (ec)) {
					if (IsConvertible (ec, orig_left, orig_right, TypeManager.bool_type)) {
						Error_OperatorAmbiguous (loc, oper, l, r);
						return null;
					}

					if (oper == Operator.BitwiseOr && l != r && !(orig_right is Constant) && right is OpcodeCast &&
						(r == TypeManager.sbyte_type || r == TypeManager.short_type ||
						 r == TypeManager.int32_type || r == TypeManager.int64_type)) {
							Report.Warning (675, 3, loc, "The operator `|' used on the sign-extended type `{0}'. Consider casting to a smaller unsigned type first",
								TypeManager.CSharpName (r));
					}
					
				} else if (!VerifyApplicable_Predefined (ec, TypeManager.bool_type)) {
					Error_OperatorCannotBeApplied ();
					return null;
				}
				return this;
			}
			
			//
			// Pointer comparison
			//
			if (l.IsPointer && r.IsPointer){
				if (oper == Operator.LessThan || oper == Operator.LessThanOrEqual ||
				    oper == Operator.GreaterThan || oper == Operator.GreaterThanOrEqual){
					type = TypeManager.bool_type;
					return this;
				}
			}

			if (OverloadResolve_PredefinedIntegral (ec)) {
				if (IsApplicable_String (ec, orig_left, orig_right, oper)) {
					Error_OperatorAmbiguous (loc, oper, l, r);
					return null;
				}
			} else if (OverloadResolve_PredefinedFloating (ec)) {
				if (IsConvertible (ec, orig_left, orig_right, TypeManager.decimal_type) ||
				    IsApplicable_String (ec, orig_left, orig_right, oper)) {
					Error_OperatorAmbiguous (loc, oper, l, r);
					return null;
				}
			} else if (VerifyApplicable_Predefined (ec, TypeManager.decimal_type)) {
				if (IsApplicable_String (ec, orig_left, orig_right, oper)) {
					Error_OperatorAmbiguous (loc, oper, l, r);
					return null;
				}
			} else if (!OverloadResolve_PredefinedString (ec, oper)) {
				Error_OperatorCannotBeApplied ();
				return null;
			}

			if (oper == Operator.Equality ||
			    oper == Operator.Inequality ||
			    oper == Operator.LessThanOrEqual ||
			    oper == Operator.LessThan ||
			    oper == Operator.GreaterThanOrEqual ||
			    oper == Operator.GreaterThan)
				type = TypeManager.bool_type;

			l = left.Type;
			r = right.Type;

			if (l == TypeManager.decimal_type || l == TypeManager.string_type || r == TypeManager.string_type) {
				Type lookup = l;
				if (r == TypeManager.string_type)
					lookup = r;
				MethodGroupExpr ops = (MethodGroupExpr) MemberLookup (
					ec.ContainerType, lookup, oper_names [(int) oper],
					MemberTypes.Method, AllBindingFlags, loc);
				ArrayList args = new ArrayList (2);
				args.Add (new Argument (left, Argument.AType.Expression));
				args.Add (new Argument (right, Argument.AType.Expression));
				ops = ops.OverloadResolve (ec, ref args, true, Location.Null);
				return new BinaryMethod (type, (MethodInfo)ops, args);
			}

			return this;
		}

		Constant EnumLiftUp (Constant left, Constant right)
		{
			switch (oper) {
			case Operator.BitwiseOr:
			case Operator.BitwiseAnd:
			case Operator.ExclusiveOr:
			case Operator.Equality:
			case Operator.Inequality:
			case Operator.LessThan:
			case Operator.LessThanOrEqual:
			case Operator.GreaterThan:
			case Operator.GreaterThanOrEqual:
				if (left is EnumConstant)
					return left;
				
				if (left.IsZeroInteger)
					return new EnumConstant (left, right.Type);
				
				break;
				
			case Operator.Addition:
			case Operator.Subtraction:
				return left;
				
			case Operator.Multiply:
			case Operator.Division:
			case Operator.Modulus:
			case Operator.LeftShift:
			case Operator.RightShift:
				if (right is EnumConstant || left is EnumConstant)
					break;
				return left;
			}
			Error_OperatorCannotBeApplied ();
			return null;
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			if (left == null)
				return null;

			if ((oper == Operator.Subtraction) && (left is ParenthesizedExpression)) {
				left = ((ParenthesizedExpression) left).Expr;
				left = left.Resolve (ec, ResolveFlags.VariableOrValue | ResolveFlags.Type);
				if (left == null)
					return null;

				if (left.eclass == ExprClass.Type) {
					Report.Error (75, loc, "To cast a negative value, you must enclose the value in parentheses");
					return null;
				}
			} else
				left = left.Resolve (ec);

			if (left == null)
				return null;

			Constant lc = left as Constant;
			if (lc != null && lc.Type == TypeManager.bool_type && 
				((oper == Operator.LogicalAnd && (bool)lc.GetValue () == false) ||
				 (oper == Operator.LogicalOr && (bool)lc.GetValue () == true))) {

				// TODO: make a sense to resolve unreachable expression as we do for statement
				Report.Warning (429, 4, loc, "Unreachable expression code detected");
				return left;
			}

			right = right.Resolve (ec);
			if (right == null)
				return null;

			eclass = ExprClass.Value;
			Constant rc = right as Constant;

			// The conversion rules are ignored in enum context but why
			if (!ec.InEnumContext && lc != null && rc != null && (TypeManager.IsEnumType (left.Type) || TypeManager.IsEnumType (right.Type))) {
				left = lc = EnumLiftUp (lc, rc);
				if (lc == null)
					return null;

				right = rc = EnumLiftUp (rc, lc);
				if (rc == null)
					return null;
			}

			if (oper == Operator.BitwiseAnd) {
				if (rc != null && rc.IsZeroInteger) {
					return lc is EnumConstant ?
						new EnumConstant (rc, lc.Type):
						rc;
				}

				if (lc != null && lc.IsZeroInteger) {
					if (rc is EnumConstant)
						return new EnumConstant (lc, rc.Type);

					//
					// Optimize cases that have no side-effects, to avoid
					// emitting code that gets popped
					//
					if (right is FieldExpr)
						return lc;

					// Side effect code:
					return new SideEffectConstant (lc, right, loc);
				}
			}
			else if (oper == Operator.BitwiseOr) {
				if (lc is EnumConstant &&
				    rc != null && rc.IsZeroInteger)
					return lc;
				if (rc is EnumConstant &&
				    lc != null && lc.IsZeroInteger)
					return rc;
			} else if (oper == Operator.LogicalAnd) {
				if (rc != null && rc.IsDefaultValue && rc.Type == TypeManager.bool_type) {
					if (lc != null)
						return rc;

					return new SideEffectConstant (rc, left, loc);
				}

				if (lc != null && lc.IsDefaultValue && lc.Type == TypeManager.bool_type)
					return lc;
			}

			if (rc != null && lc != null){
				int prev_e = Report.Errors;
				Expression e = ConstantFold.BinaryFold (
					ec, oper, lc, rc, loc);
				if (e != null || Report.Errors != prev_e)
					return e;
			}

#if GMCS_SOURCE
			if ((left is NullLiteral || left.Type.IsValueType) &&
			    (right is NullLiteral || right.Type.IsValueType) &&
			    !(left is NullLiteral && right is NullLiteral) &&
			    (TypeManager.IsNullableType (left.Type) || TypeManager.IsNullableType (right.Type)))
				return new Nullable.LiftedBinaryOperator (oper, left, right, loc).Resolve (ec);
#endif

			// Comparison warnings
			if (oper == Operator.Equality || oper == Operator.Inequality ||
			    oper == Operator.LessThanOrEqual || oper == Operator.LessThan ||
			    oper == Operator.GreaterThanOrEqual || oper == Operator.GreaterThan){
				if (left.Equals (right)) {
					Report.Warning (1718, 3, loc, "A comparison made to same variable. Did you mean to compare something else?");
				}
				CheckUselessComparison (lc, right.Type);
				CheckUselessComparison (rc, left.Type);
			}

			return ResolveOperator (ec);
		}

		public override TypeExpr ResolveAsTypeTerminal (IResolveContext ec, bool silent)
		{
			return null;
		}

		private void CheckUselessComparison (Constant c, Type type)
		{
			if (c == null || !IsTypeIntegral (type)
				|| c is StringConstant
				|| c is BoolConstant
				|| c is FloatConstant
				|| c is DoubleConstant
				|| c is DecimalConstant
				)
				return;

			long value = 0;

			if (c is ULongConstant) {
				ulong uvalue = ((ULongConstant) c).Value;
				if (uvalue > long.MaxValue) {
					if (type == TypeManager.byte_type ||
					    type == TypeManager.sbyte_type ||
					    type == TypeManager.short_type ||
					    type == TypeManager.ushort_type ||
					    type == TypeManager.int32_type ||
					    type == TypeManager.uint32_type ||
					    type == TypeManager.int64_type ||
						type == TypeManager.char_type)
						WarnUselessComparison (type);
					return;
				}
				value = (long) uvalue;
			}
			else if (c is ByteConstant)
				value = ((ByteConstant) c).Value;
			else if (c is SByteConstant)
				value = ((SByteConstant) c).Value;
			else if (c is ShortConstant)
				value = ((ShortConstant) c).Value;
			else if (c is UShortConstant)
				value = ((UShortConstant) c).Value;
			else if (c is IntConstant)
				value = ((IntConstant) c).Value;
			else if (c is UIntConstant)
				value = ((UIntConstant) c).Value;
			else if (c is LongConstant)
				value = ((LongConstant) c).Value;
			else if (c is CharConstant)
				value = ((CharConstant)c).Value;

			if (value == 0)
				return;

			if (IsValueOutOfRange (value, type))
				WarnUselessComparison (type);
		}

		private bool IsValueOutOfRange (long value, Type type)
		{
			if (IsTypeUnsigned (type) && value < 0)
				return true;
			return type == TypeManager.sbyte_type && (value >= 0x80 || value < -0x80) ||
				type == TypeManager.byte_type && value >= 0x100 ||
				type == TypeManager.short_type && (value >= 0x8000 || value < -0x8000) ||
				type == TypeManager.ushort_type && value >= 0x10000 ||
				type == TypeManager.int32_type && (value >= 0x80000000 || value < -0x80000000) ||
				type == TypeManager.uint32_type && value >= 0x100000000;
		}

		private static bool IsTypeIntegral (Type type)
		{
			return type == TypeManager.uint64_type ||
				type == TypeManager.int64_type ||
				type == TypeManager.uint32_type ||
				type == TypeManager.int32_type ||
				type == TypeManager.ushort_type ||
				type == TypeManager.short_type ||
				type == TypeManager.sbyte_type ||
				type == TypeManager.byte_type ||
				type == TypeManager.char_type;
		}

		private static bool IsTypeUnsigned (Type type)
		{
			return type == TypeManager.uint64_type ||
				type == TypeManager.uint32_type ||
				type == TypeManager.ushort_type ||
				type == TypeManager.byte_type ||
				type == TypeManager.char_type;
		}

		private void WarnUselessComparison (Type type)
		{
			Report.Warning (652, 2, loc, "A comparison between a constant and a variable is useless. The constant is out of the range of the variable type `{0}'",
				TypeManager.CSharpName (type));
		}

		/// <remarks>
		///   EmitBranchable is called from Statement.EmitBoolExpression in the
		///   context of a conditional bool expression.  This function will return
		///   false if it is was possible to use EmitBranchable, or true if it was.
		///
		///   The expression's code is generated, and we will generate a branch to `target'
		///   if the resulting expression value is equal to isTrue
		/// </remarks>
		public override void EmitBranchable (EmitContext ec, Label target, bool on_true)
		{
			ILGenerator ig = ec.ig;

			//
			// This is more complicated than it looks, but its just to avoid
			// duplicated tests: basically, we allow ==, !=, >, <, >= and <=
			// but on top of that we want for == and != to use a special path
			// if we are comparing against null
			//
			if ((oper == Operator.Equality || oper == Operator.Inequality) && (left is Constant || right is Constant)) {
				bool my_on_true = oper == Operator.Inequality ? on_true : !on_true;
				
				//
				// put the constant on the rhs, for simplicity
				//
				if (left is Constant) {
					Expression swap = right;
					right = left;
					left = swap;
				}
				
				if (((Constant) right).IsZeroInteger) {
					left.Emit (ec);
					if (my_on_true)
						ig.Emit (OpCodes.Brtrue, target);
					else
						ig.Emit (OpCodes.Brfalse, target);
					
					return;
				} else if (right is BoolConstant) {
					left.Emit (ec);
					if (my_on_true != ((BoolConstant) right).Value)
						ig.Emit (OpCodes.Brtrue, target);
					else
						ig.Emit (OpCodes.Brfalse, target);
					
					return;
				}

			} else if (oper == Operator.LogicalAnd) {

				if (on_true) {
					Label tests_end = ig.DefineLabel ();
					
					left.EmitBranchable (ec, tests_end, false);
					right.EmitBranchable (ec, target, true);
					ig.MarkLabel (tests_end);					
				} else {
					//
					// This optimizes code like this 
					// if (true && i > 4)
					//
					if (!(left is Constant))
						left.EmitBranchable (ec, target, false);

					if (!(right is Constant)) 
						right.EmitBranchable (ec, target, false);
				}
				
				return;
				
			} else if (oper == Operator.LogicalOr){
				if (on_true) {
					left.EmitBranchable (ec, target, true);
					right.EmitBranchable (ec, target, true);
					
				} else {
					Label tests_end = ig.DefineLabel ();
					left.EmitBranchable (ec, tests_end, true);
					right.EmitBranchable (ec, target, false);
					ig.MarkLabel (tests_end);
				}
				
				return;
				
			} else if (!(oper == Operator.LessThan        || oper == Operator.GreaterThan ||
			             oper == Operator.LessThanOrEqual || oper == Operator.GreaterThanOrEqual ||
			             oper == Operator.Equality        || oper == Operator.Inequality)) {
				base.EmitBranchable (ec, target, on_true);
				return;
			}
			
			left.Emit (ec);
			right.Emit (ec);

			Type t = left.Type;
			bool is_unsigned = IsUnsigned (t) || t == TypeManager.double_type || t == TypeManager.float_type;
			
			switch (oper){
			case Operator.Equality:
				if (on_true)
					ig.Emit (OpCodes.Beq, target);
				else
					ig.Emit (OpCodes.Bne_Un, target);
				break;

			case Operator.Inequality:
				if (on_true)
					ig.Emit (OpCodes.Bne_Un, target);
				else
					ig.Emit (OpCodes.Beq, target);
				break;

			case Operator.LessThan:
				if (on_true)
					if (is_unsigned)
						ig.Emit (OpCodes.Blt_Un, target);
					else
						ig.Emit (OpCodes.Blt, target);
				else
					if (is_unsigned)
						ig.Emit (OpCodes.Bge_Un, target);
					else
						ig.Emit (OpCodes.Bge, target);
				break;

			case Operator.GreaterThan:
				if (on_true)
					if (is_unsigned)
						ig.Emit (OpCodes.Bgt_Un, target);
					else
						ig.Emit (OpCodes.Bgt, target);
				else
					if (is_unsigned)
						ig.Emit (OpCodes.Ble_Un, target);
					else
						ig.Emit (OpCodes.Ble, target);
				break;

			case Operator.LessThanOrEqual:
				if (on_true)
					if (is_unsigned)
						ig.Emit (OpCodes.Ble_Un, target);
					else
						ig.Emit (OpCodes.Ble, target);
				else
					if (is_unsigned)
						ig.Emit (OpCodes.Bgt_Un, target);
					else
						ig.Emit (OpCodes.Bgt, target);
				break;


			case Operator.GreaterThanOrEqual:
				if (on_true)
					if (is_unsigned)
						ig.Emit (OpCodes.Bge_Un, target);
					else
						ig.Emit (OpCodes.Bge, target);
				else
					if (is_unsigned)
						ig.Emit (OpCodes.Blt_Un, target);
					else
						ig.Emit (OpCodes.Blt, target);
				break;
			default:
				Console.WriteLine (oper);
				throw new Exception ("what is THAT");
			}
		}
		
		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;
			Type l = left.Type;
			OpCode opcode;

			//
			// Handle short-circuit operators differently
			// than the rest
			//
			if (oper == Operator.LogicalAnd) {
				Label load_zero = ig.DefineLabel ();
				Label end = ig.DefineLabel ();
								
				left.EmitBranchable (ec, load_zero, false);
				right.Emit (ec);
				ig.Emit (OpCodes.Br, end);
				
				ig.MarkLabel (load_zero);
				ig.Emit (OpCodes.Ldc_I4_0);
				ig.MarkLabel (end);
				return;
			} else if (oper == Operator.LogicalOr) {
				Label load_one = ig.DefineLabel ();
				Label end = ig.DefineLabel ();

				left.EmitBranchable (ec, load_one, true);
				right.Emit (ec);
				ig.Emit (OpCodes.Br, end);
				
				ig.MarkLabel (load_one);
				ig.Emit (OpCodes.Ldc_I4_1);
				ig.MarkLabel (end);
				return;
			}

			left.Emit (ec);
			right.Emit (ec);

			bool is_unsigned = IsUnsigned (left.Type);
			
			switch (oper){
			case Operator.Multiply:
				if (ec.CheckState){
					if (l == TypeManager.int32_type || l == TypeManager.int64_type)
						opcode = OpCodes.Mul_Ovf;
					else if (is_unsigned)
						opcode = OpCodes.Mul_Ovf_Un;
					else
						opcode = OpCodes.Mul;
				} else
					opcode = OpCodes.Mul;
				
				break;
				
			case Operator.Division:
				if (is_unsigned)
					opcode = OpCodes.Div_Un;
				else
					opcode = OpCodes.Div;
				break;
				
			case Operator.Modulus:
				if (is_unsigned)
					opcode = OpCodes.Rem_Un;
				else
					opcode = OpCodes.Rem;
				break;

			case Operator.Addition:
				if (ec.CheckState){
					if (l == TypeManager.int32_type || l == TypeManager.int64_type)
						opcode = OpCodes.Add_Ovf;
					else if (is_unsigned)
						opcode = OpCodes.Add_Ovf_Un;
					else
						opcode = OpCodes.Add;
				} else
					opcode = OpCodes.Add;
				break;

			case Operator.Subtraction:
				if (ec.CheckState){
					if (l == TypeManager.int32_type || l == TypeManager.int64_type)
						opcode = OpCodes.Sub_Ovf;
					else if (is_unsigned)
						opcode = OpCodes.Sub_Ovf_Un;
					else
						opcode = OpCodes.Sub;
				} else
					opcode = OpCodes.Sub;
				break;

			case Operator.RightShift:
				if (is_unsigned)
					opcode = OpCodes.Shr_Un;
				else
					opcode = OpCodes.Shr;
				break;
				
			case Operator.LeftShift:
				opcode = OpCodes.Shl;
				break;

			case Operator.Equality:
				opcode = OpCodes.Ceq;
				break;

			case Operator.Inequality:
				ig.Emit (OpCodes.Ceq);
				ig.Emit (OpCodes.Ldc_I4_0);
				
				opcode = OpCodes.Ceq;
				break;

			case Operator.LessThan:
				if (is_unsigned)
					opcode = OpCodes.Clt_Un;
				else
					opcode = OpCodes.Clt;
				break;

			case Operator.GreaterThan:
				if (is_unsigned)
					opcode = OpCodes.Cgt_Un;
				else
					opcode = OpCodes.Cgt;
				break;

			case Operator.LessThanOrEqual:
				Type lt = left.Type;
				
				if (is_unsigned || (lt == TypeManager.double_type || lt == TypeManager.float_type))
					ig.Emit (OpCodes.Cgt_Un);
				else
					ig.Emit (OpCodes.Cgt);
				ig.Emit (OpCodes.Ldc_I4_0);
				
				opcode = OpCodes.Ceq;
				break;

			case Operator.GreaterThanOrEqual:
				Type le = left.Type;
				
				if (is_unsigned || (le == TypeManager.double_type || le == TypeManager.float_type))
					ig.Emit (OpCodes.Clt_Un);
				else
					ig.Emit (OpCodes.Clt);
				
				ig.Emit (OpCodes.Ldc_I4_0);
				
				opcode = OpCodes.Ceq;
				break;

			case Operator.BitwiseOr:
				opcode = OpCodes.Or;
				break;

			case Operator.BitwiseAnd:
				opcode = OpCodes.And;
				break;

			case Operator.ExclusiveOr:
				opcode = OpCodes.Xor;
				break;

			default:
				throw new Exception ("This should not happen: Operator = "
						     + oper.ToString ());
			}

			ig.Emit (opcode);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Binary target = (Binary) t;

			target.left = left.Clone (clonectx);
			target.right = right.Clone (clonectx);
		}
		
		public override Expression CreateExpressionTree (EmitContext ec)
		{
			return CreateExpressionTree (ec, null);
		}

		Expression CreateExpressionTree (EmitContext ec, MethodGroupExpr method)		
		{
			string method_name;
			bool lift_arg = false;
			
			switch (oper) {
			case Operator.Addition:
				if (method == null && ec.CheckState)
					method_name = "AddChecked";
				else
					method_name = "Add";
				break;
			case Operator.BitwiseAnd:
				method_name = "And";
				break;
			case Operator.Division:
				method_name = "Divide";
				break;
			case Operator.Equality:
				method_name = "Equal";
				lift_arg = true;
				break;
			case Operator.ExclusiveOr:
				method_name = "ExclusiveOr";
				break;				
			case Operator.GreaterThan:
				method_name = "GreaterThan";
				lift_arg = true;
				break;
			case Operator.GreaterThanOrEqual:
				method_name = "GreaterThanOrEqual";
				lift_arg = true;
				break;				
			case Operator.LessThan:
				method_name = "LessThan";
				break;
			case Operator.LogicalAnd:
				method_name = "AndAlso";
				break;
				
			case Operator.BitwiseOr:
				method_name = "Or";
				break;

			case Operator.LogicalOr:
				method_name = "OrElse";
				break;
			default:
				throw new InternalErrorException ("Unknown expression tree binary operator " + oper);
			}

			ArrayList args = new ArrayList (2);
			args.Add (new Argument (left.CreateExpressionTree (ec)));
			args.Add (new Argument (right.CreateExpressionTree (ec)));
			if (method != null) {
				if (lift_arg)
					args.Add (new Argument (new BoolConstant (false, loc)));
				
				args.Add (new Argument (method.CreateExpressionTree (ec)));
			}
			
			return CreateExpressionFactoryCall (method_name, args);
		}
	}

	//
	// Object created by Binary when the binary operator uses an method instead of being
	// a binary operation that maps to a CIL binary operation.
	//
	public class BinaryMethod : Expression {
		public MethodBase method;
		public ArrayList  Arguments;
		
		public BinaryMethod (Type t, MethodBase m, ArrayList args)
		{
			method = m;
			Arguments = args;
			type = t;
			eclass = ExprClass.Value;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;
			
			Invocation.EmitArguments (ec, Arguments, false, null);
			
			if (method is MethodInfo)
				ig.Emit (OpCodes.Call, (MethodInfo) method);
			else
				ig.Emit (OpCodes.Call, (ConstructorInfo) method);
		}
	}
	
	//
	// Represents the operation a + b [+ c [+ d [+ ...]]], where a is a string
	// b, c, d... may be strings or objects.
	//
	public class StringConcat : Expression {
		ArrayList arguments;
		
		public StringConcat (EmitContext ec, Location loc, Expression left, Expression right)
		{
			this.loc = loc;
			type = TypeManager.string_type;
			eclass = ExprClass.Value;

			arguments = new ArrayList (2);
			Append (ec, left);
			Append (ec, right);
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}
		
		public void Append (EmitContext ec, Expression operand)
		{
			//
			// Constant folding
			//
			StringConstant sc = operand as StringConstant;
			if (sc != null) {
				if (arguments.Count != 0) {
					Argument last_argument = (Argument) arguments [arguments.Count - 1];
					StringConstant last_expr_constant = last_argument.Expr as StringConstant;
					if (last_expr_constant != null) {
						last_argument.Expr = new StringConstant (
							last_expr_constant.Value + sc.Value, sc.Location);
						return;
					}
				}
			} else {
				//
				// Multiple (3+) concatenation are resolved as multiple StringConcat instances
				//
				StringConcat concat_oper = operand as StringConcat;
				if (concat_oper != null) {
					arguments.AddRange (concat_oper.arguments);
					return;
				}
			}
			
			//
			// Conversion to object
			//
			if (operand.Type != TypeManager.string_type) {
				Expression expr = Convert.ImplicitConversion (ec, operand, TypeManager.object_type, loc);
				if (expr == null) {
					Binary.Error_OperatorCannotBeApplied (loc, "+", TypeManager.string_type, operand.Type);
					return;
				}
				operand = expr;
			}
			
			arguments.Add (new Argument (operand));
		}

		Expression CreateConcatInvocation ()
		{
			return new Invocation (
				new MemberAccess (new MemberAccess (new QualifiedAliasMember ("global", "System", loc), "String", loc), "Concat", loc),
				arguments, true);
		}

		public override void Emit (EmitContext ec)
		{
			Expression concat = CreateConcatInvocation ();
			concat = concat.Resolve (ec);
			if (concat != null)
				concat.Emit (ec);
		}
	}

	//
	// Object created with +/= on delegates
	//
	public class BinaryDelegate : Expression {
		MethodInfo method;
		ArrayList  args;

		public BinaryDelegate (Type t, MethodInfo mi, ArrayList args)
		{
			method = mi;
			this.args = args;
			type = t;
			eclass = ExprClass.Value;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;
			
			Invocation.EmitArguments (ec, args, false, null);
			
			ig.Emit (OpCodes.Call, (MethodInfo) method);
			ig.Emit (OpCodes.Castclass, type);
		}

		public Expression Right {
			get {
				Argument arg = (Argument) args [1];
				return arg.Expr;
			}
		}

		public bool IsAddition {
			get {
				return method == TypeManager.delegate_combine_delegate_delegate;
			}
		}
	}
	
	//
	// User-defined conditional logical operator
	//
	public class ConditionalLogicalOperator : Expression {
		Expression left, right;
		bool is_and;
		Expression op_true, op_false;
		UserOperatorCall op;
		LocalTemporary left_temp;

		public ConditionalLogicalOperator (bool is_and, Expression left, Expression right, Type t, Location loc)
		{
			type = t;
			eclass = ExprClass.Value;
			this.loc = loc;
			this.left = left;
			this.right = right;
			this.is_and = is_and;
		}
		
		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (3);
			args.Add (new Argument (left.CreateExpressionTree (ec)));
			args.Add (new Argument (right.CreateExpressionTree (ec)));
			args.Add (new Argument (op.Method.CreateExpressionTree (ec)));
			return CreateExpressionFactoryCall (is_and ? "AndAlso" : "OrAlso", args);
		}		

		protected void Error19 ()
		{
			Binary.Error_OperatorCannotBeApplied (loc, is_and ? "&&" : "||", left.GetSignatureForError (), right.GetSignatureForError ());
		}

		protected void Error218 ()
		{
			Error (218, "The type ('" + TypeManager.CSharpName (type) + "') must contain " +
			       "declarations of operator true and operator false");
		}

		public override Expression DoResolve (EmitContext ec)
		{
			MethodGroupExpr operator_group;

			operator_group = MethodLookup (ec.ContainerType, type, is_and ? "op_BitwiseAnd" : "op_BitwiseOr", loc) as MethodGroupExpr;
			if (operator_group == null) {
				Error19 ();
				return null;
			}

			left_temp = new LocalTemporary (type);

			ArrayList arguments = new ArrayList (2);
			arguments.Add (new Argument (left_temp, Argument.AType.Expression));
			arguments.Add (new Argument (right, Argument.AType.Expression));
			operator_group = operator_group.OverloadResolve (ec, ref arguments, false, loc);
			if (operator_group == null) {
				Error19 ();
				return null;
			}

			MethodInfo method = (MethodInfo)operator_group;
			if (method.ReturnType != type) {
				Report.Error (217, loc, "In order to be applicable as a short circuit operator a user-defined logical operator `{0}' " +
						"must have the same return type as the type of its 2 parameters", TypeManager.CSharpSignature (method));
				return null;
			}

			op = new UserOperatorCall (operator_group, arguments, null, loc);

			op_true = GetOperatorTrue (ec, left_temp, loc);
			op_false = GetOperatorFalse (ec, left_temp, loc);
			if ((op_true == null) || (op_false == null)) {
				Error218 ();
				return null;
			}

			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;
			Label false_target = ig.DefineLabel ();
			Label end_target = ig.DefineLabel ();

			left.Emit (ec);
			left_temp.Store (ec);

			(is_and ? op_false : op_true).EmitBranchable (ec, false_target, false);
			left_temp.Emit (ec);
			ig.Emit (OpCodes.Br, end_target);
			ig.MarkLabel (false_target);
			op.Emit (ec);
			ig.MarkLabel (end_target);

			// We release 'left_temp' here since 'op' may refer to it too
			left_temp.Release (ec);
		}
	}

	public class PointerArithmetic : Expression {
		Expression left, right;
		bool is_add;

		//
		// We assume that `l' is always a pointer
		//
		public PointerArithmetic (bool is_addition, Expression l, Expression r, Type t, Location loc)
		{
			type = t;
			this.loc = loc;
			left = l;
			right = r;
			is_add = is_addition;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			eclass = ExprClass.Variable;
			
			if (left.Type == TypeManager.void_ptr_type) {
				Error (242, "The operation in question is undefined on void pointers");
				return null;
			}
			
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			Type op_type = left.Type;
			ILGenerator ig = ec.ig;
			
			// It must be either array or fixed buffer
			Type element = TypeManager.HasElementType (op_type) ?
				element = TypeManager.GetElementType (op_type) :
				element = AttributeTester.GetFixedBuffer (((FieldExpr)left).FieldInfo).ElementType;

			int size = GetTypeSize (element);
			Type rtype = right.Type;
			
			if (rtype.IsPointer){
				//
				// handle (pointer - pointer)
				//
				left.Emit (ec);
				right.Emit (ec);
				ig.Emit (OpCodes.Sub);

				if (size != 1){
					if (size == 0)
						ig.Emit (OpCodes.Sizeof, element);
					else 
						IntLiteral.EmitInt (ig, size);
					ig.Emit (OpCodes.Div);
				}
				ig.Emit (OpCodes.Conv_I8);
			} else {
				//
				// handle + and - on (pointer op int)
				//
				left.Emit (ec);
				ig.Emit (OpCodes.Conv_I);

				Constant right_const = right as Constant;
				if (right_const != null && size != 0) {
					Expression ex = ConstantFold.BinaryFold (ec, Binary.Operator.Multiply, new IntConstant (size, right.Location), right_const, loc);
					if (ex == null)
						return;
					ex.Emit (ec);
				} else {
					right.Emit (ec);
					if (size != 1){
						if (size == 0)
							ig.Emit (OpCodes.Sizeof, element);
						else 
							IntLiteral.EmitInt (ig, size);
						if (rtype == TypeManager.int64_type)
							ig.Emit (OpCodes.Conv_I8);
						else if (rtype == TypeManager.uint64_type)
							ig.Emit (OpCodes.Conv_U8);
						ig.Emit (OpCodes.Mul);
					}
				}
				
				if (rtype == TypeManager.int64_type || rtype == TypeManager.uint64_type)
					ig.Emit (OpCodes.Conv_I);
				
				if (is_add)
					ig.Emit (OpCodes.Add);
				else
					ig.Emit (OpCodes.Sub);
			}
		}
	}
	
	/// <summary>
	///   Implements the ternary conditional operator (?:)
	/// </summary>
	public class Conditional : Expression {
		Expression expr, true_expr, false_expr;
		
		public Conditional (Expression expr, Expression true_expr, Expression false_expr)
		{
			this.expr = expr;
			this.true_expr = true_expr;
			this.false_expr = false_expr;
			this.loc = expr.Location;
		}

		public Expression Expr {
			get {
				return expr;
			}
		}

		public Expression TrueExpr {
			get {
				return true_expr;
			}
		}

		public Expression FalseExpr {
			get {
				return false_expr;
			}
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (3);
			args.Add (new Argument (expr.CreateExpressionTree (ec)));
			args.Add (new Argument (true_expr.CreateExpressionTree (ec)));
			args.Add (new Argument (false_expr.CreateExpressionTree (ec)));
			return CreateExpressionFactoryCall ("Condition", args);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			expr = expr.Resolve (ec);

			if (expr == null)
				return null;

			if (expr.Type != TypeManager.bool_type){
				expr = Expression.ResolveBoolean (
					ec, expr, loc);
				
				if (expr == null)
					return null;
			}
			
			Assign ass = expr as Assign;
			if (ass != null && ass.Source is Constant) {
				Report.Warning (665, 3, loc, "Assignment in conditional expression is always constant; did you mean to use == instead of = ?");
			}

			true_expr = true_expr.Resolve (ec);
			false_expr = false_expr.Resolve (ec);

			if (true_expr == null || false_expr == null)
				return null;

			eclass = ExprClass.Value;
			if (true_expr.Type == false_expr.Type) {
				type = true_expr.Type;
				if (type == TypeManager.null_type) {
					// TODO: probably will have to implement ConditionalConstant
					// to call method without return constant as well
					Report.Warning (-101, 1, loc, "Conditional expression will always return same value");
					return true_expr;
				}
			} else {
				Expression conv;
				Type true_type = true_expr.Type;
				Type false_type = false_expr.Type;

				//
				// First, if an implicit conversion exists from true_expr
				// to false_expr, then the result type is of type false_expr.Type
				//
				conv = Convert.ImplicitConversion (ec, true_expr, false_type, loc);
				if (conv != null){
					//
					// Check if both can convert implicitl to each other's type
					//
					if (Convert.ImplicitConversion (ec, false_expr, true_type, loc) != null){
						Error (172,
						       "Can not compute type of conditional expression " +
						       "as `" + TypeManager.CSharpName (true_expr.Type) +
						       "' and `" + TypeManager.CSharpName (false_expr.Type) +
						       "' convert implicitly to each other");
						return null;
					}
					type = false_type;
					true_expr = conv;
				} else if ((conv = Convert.ImplicitConversion(ec, false_expr, true_type,loc))!= null){
					type = true_type;
					false_expr = conv;
				} else {
					Report.Error (173, loc, "Type of conditional expression cannot be determined because there is no implicit conversion between `{0}' and `{1}'",
						true_expr.GetSignatureForError (), false_expr.GetSignatureForError ());
					return null;
				}
			}

			// Dead code optimalization
			if (expr is BoolConstant){
				BoolConstant bc = (BoolConstant) expr;

				Report.Warning (429, 4, bc.Value ? false_expr.Location : true_expr.Location, "Unreachable expression code detected");
				return bc.Value ? true_expr : false_expr;
			}

			return this;
		}

		public override TypeExpr ResolveAsTypeTerminal (IResolveContext ec, bool silent)
		{
			return null;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;
			Label false_target = ig.DefineLabel ();
			Label end_target = ig.DefineLabel ();

			expr.EmitBranchable (ec, false_target, false);
			true_expr.Emit (ec);

			if (type.IsInterface) {
				LocalBuilder temp = ec.GetTemporaryLocal (type);
				ig.Emit (OpCodes.Stloc, temp);
				ig.Emit (OpCodes.Ldloc, temp);
				ec.FreeTemporaryLocal (temp, type);
			}

			ig.Emit (OpCodes.Br, end_target);
			ig.MarkLabel (false_target);
			false_expr.Emit (ec);
			ig.MarkLabel (end_target);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Conditional target = (Conditional) t;

			target.expr = expr.Clone (clonectx);
			target.true_expr = true_expr.Clone (clonectx);
			target.false_expr = false_expr.Clone (clonectx);
		}
	}

	public abstract class VariableReference : Expression, IAssignMethod, IMemoryLocation {
		bool prepared;
		LocalTemporary temp;

		public abstract Variable Variable {
			get;
		}

		public abstract bool IsRef {
			get;
		}

		public override void Emit (EmitContext ec)
		{
			Emit (ec, false);
		}

		//
		// This method is used by parameters that are references, that are
		// being passed as references:  we only want to pass the pointer (that
		// is already stored in the parameter, not the address of the pointer,
		// and not the value of the variable).
		//
		public void EmitLoad (EmitContext ec)
		{
			Report.Debug (64, "VARIABLE EMIT LOAD", this, Variable, type, loc);
			if (!prepared)
				Variable.EmitInstance (ec);
			Variable.Emit (ec);
		}
		
		public void Emit (EmitContext ec, bool leave_copy)
		{
			Report.Debug (64, "VARIABLE EMIT", this, Variable, type, IsRef, loc);

			EmitLoad (ec);

			if (IsRef) {
				//
				// If we are a reference, we loaded on the stack a pointer
				// Now lets load the real value
				//
				LoadFromPtr (ec.ig, type);
			}

			if (leave_copy) {
				ec.ig.Emit (OpCodes.Dup);

				if (IsRef || Variable.NeedsTemporary) {
					temp = new LocalTemporary (Type);
					temp.Store (ec);
				}
			}
		}

		public void EmitAssign (EmitContext ec, Expression source, bool leave_copy,
					bool prepare_for_load)
		{
			Report.Debug (64, "VARIABLE EMIT ASSIGN", this, Variable, type, IsRef,
				      source, loc);

			ILGenerator ig = ec.ig;
			prepared = prepare_for_load;

			Variable.EmitInstance (ec);
			if (prepare_for_load) {
				if (Variable.HasInstance)
					ig.Emit (OpCodes.Dup);
			}

			if (IsRef)
				Variable.Emit (ec);

			source.Emit (ec);

			// HACK: variable is already emitted when source is an initializer 
			if (source is NewInitialize)
				return;

			if (leave_copy) {
				ig.Emit (OpCodes.Dup);
				if (IsRef || Variable.NeedsTemporary) {
					temp = new LocalTemporary (Type);
					temp.Store (ec);
				}
			}

			if (IsRef)
				StoreFromPtr (ig, type);
			else
				Variable.EmitAssign (ec);

			if (temp != null) {
				temp.Emit (ec);
				temp.Release (ec);
			}
		}
		
		public void AddressOf (EmitContext ec, AddressOp mode)
		{
			Variable.EmitInstance (ec);
			Variable.EmitAddressOf (ec);
		}
	}

	/// <summary>
	///   Local variables
	/// </summary>
	public class LocalVariableReference : VariableReference, IVariable {
		public readonly string Name;
		public Block Block;
		public LocalInfo local_info;
		bool is_readonly;
		Variable variable;

		public LocalVariableReference (Block block, string name, Location l)
		{
			Block = block;
			Name = name;
			loc = l;
			eclass = ExprClass.Variable;
		}

		//
		// Setting `is_readonly' to false will allow you to create a writable
		// reference to a read-only variable.  This is used by foreach and using.
		//
		public LocalVariableReference (Block block, string name, Location l,
					       LocalInfo local_info, bool is_readonly)
			: this (block, name, l)
		{
			this.local_info = local_info;
			this.is_readonly = is_readonly;
		}

		public VariableInfo VariableInfo {
			get { return local_info.VariableInfo; }
		}

		public override bool IsRef {
			get { return false; }
		}

		public bool IsReadOnly {
			get { return is_readonly; }
		}

		public bool VerifyAssigned (EmitContext ec)
		{
			VariableInfo variable_info = local_info.VariableInfo;
			return variable_info == null || variable_info.IsAssigned (ec, loc);
		}

		void ResolveLocalInfo ()
		{
			if (local_info == null) {
				local_info = Block.GetLocalInfo (Name);
				type = local_info.VariableType;
				is_readonly = local_info.ReadOnly;
			}
		}

		protected Expression DoResolveBase (EmitContext ec)
		{
			type = local_info.VariableType;

			Expression e = Block.GetConstantExpression (Name);
			if (e != null)
				return e.Resolve (ec);

			if (!VerifyAssigned (ec))
				return null;

			//
			// If we are referencing a variable from the external block
			// flag it for capturing
			//
			if (ec.MustCaptureVariable (local_info)) {
				if (local_info.AddressTaken){
					AnonymousMethod.Error_AddressOfCapturedVar (local_info.Name, loc);
					return null;
				}

				if (!ec.IsInProbingMode)
				{
					ScopeInfo scope = local_info.Block.CreateScopeInfo ();
					variable = scope.AddLocal (local_info);
					type = variable.Type;
				}
			}

			return this;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			ResolveLocalInfo ();
			local_info.Used = true;

			if (type == null && local_info.Type is VarExpr) {
			    local_info.VariableType = TypeManager.object_type;
				Error_VariableIsUsedBeforeItIsDeclared (Name);
			    return null;
			}
			
			return DoResolveBase (ec);
		}

		override public Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			ResolveLocalInfo ();

			// is out param
			if (right_side == EmptyExpression.OutAccess)
				local_info.Used = true;

			// Infer implicitly typed local variable
			if (type == null) {
				VarExpr ve = local_info.Type as VarExpr;
				if (ve != null) {
					ve.DoResolveLValue (ec, right_side);
					type = local_info.VariableType = ve.Type;
				}
			}
						
			if (is_readonly) {
				int code;
				string msg;
				if (right_side == EmptyExpression.OutAccess) {
					code = 1657; msg = "Cannot pass `{0}' as a ref or out argument because it is a `{1}'";
				} else if (right_side == EmptyExpression.LValueMemberAccess) {
					code = 1654; msg = "Cannot assign to members of `{0}' because it is a `{1}'";
				} else if (right_side == EmptyExpression.LValueMemberOutAccess) {
					code = 1655; msg = "Cannot pass members of `{0}' as ref or out arguments because it is a `{1}'";
				} else {
					code = 1656; msg = "Cannot assign to `{0}' because it is a `{1}'";
				}
				Report.Error (code, loc, msg, Name, local_info.GetReadOnlyContext ());
				return null;
			}

			if (VariableInfo != null)
				VariableInfo.SetAssigned (ec);

			return DoResolveBase (ec);
		}

		public bool VerifyFixed ()
		{
			// A local Variable is always fixed.
			return true;
		}

		public override int GetHashCode ()
		{
			return Name.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			LocalVariableReference lvr = obj as LocalVariableReference;
			if (lvr == null)
				return false;

			return Name == lvr.Name && Block == lvr.Block;
		}

		public override Variable Variable {
			get { return variable != null ? variable : local_info.Variable; }
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), Name, loc);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			LocalVariableReference target = (LocalVariableReference) t;
			
			target.Block = clonectx.LookupBlock (Block);
			if (local_info != null)
				target.local_info = clonectx.LookupVariable (local_info);
		}
	}

	/// <summary>
	///   This represents a reference to a parameter in the intermediate
	///   representation.
	/// </summary>
	public class ParameterReference : VariableReference, IVariable {
		readonly ToplevelParameterInfo pi;
		readonly ToplevelBlock referenced;
		Variable variable;

		public bool is_ref, is_out;

		public bool IsOut {
			get { return is_out; }
		}

		public override bool IsRef {
			get { return is_ref; }
		}

		public string Name {
			get { return Parameter.Name; }
		}

		public Parameter Parameter {
			get { return pi.Parameter; }
		}

		public ParameterReference (ToplevelBlock referenced, ToplevelParameterInfo pi, Location loc)
		{
			this.pi = pi;
			this.referenced = referenced;
			this.loc = loc;
			eclass = ExprClass.Variable;
		}

		public VariableInfo VariableInfo {
			get { return pi.VariableInfo; }
		}

		public override Variable Variable {
			get { return variable != null ? variable : Parameter.Variable; }
		}

		public bool VerifyFixed ()
		{
			// A parameter is fixed if it's a value parameter (i.e., no modifier like out, ref, param).
			return Parameter.ModFlags == Parameter.Modifier.NONE;
		}

		public bool IsAssigned (EmitContext ec, Location loc)
		{
			// HACK: Variables are not captured in probing mode
			if (ec.IsInProbingMode)
				return true;
			
			if (!ec.DoFlowAnalysis || !is_out || ec.CurrentBranching.IsAssigned (VariableInfo))
				return true;

			Report.Error (269, loc, "Use of unassigned out parameter `{0}'", Name);
			return false;
		}

		public bool IsFieldAssigned (EmitContext ec, string field_name, Location loc)
		{
			if (!ec.DoFlowAnalysis || !is_out || ec.CurrentBranching.IsFieldAssigned (VariableInfo, field_name))
				return true;

			Report.Error (170, loc, "Use of possibly unassigned field `{0}'", field_name);
			return false;
		}

		public void SetAssigned (EmitContext ec)
		{
			if (is_out && ec.DoFlowAnalysis)
				ec.CurrentBranching.SetAssigned (VariableInfo);
		}

		public void SetFieldAssigned (EmitContext ec, string field_name)
		{
			if (is_out && ec.DoFlowAnalysis)
				ec.CurrentBranching.SetFieldAssigned (VariableInfo, field_name);
		}

		protected bool DoResolveBase (EmitContext ec)
		{
			Parameter par = Parameter;
			if (!par.Resolve (ec)) {
				//TODO:
			}

			type = par.ParameterType;
			Parameter.Modifier mod = par.ModFlags;
			is_ref = (mod & Parameter.Modifier.ISBYREF) != 0;
			is_out = (mod & Parameter.Modifier.OUT) == Parameter.Modifier.OUT;
			eclass = ExprClass.Variable;

			AnonymousContainer am = ec.CurrentAnonymousMethod;
			if (am == null)
				return true;

			ToplevelBlock declared = pi.Block;
			if (is_ref && declared != referenced) {
				Report.Error (1628, Location,
					      "Cannot use ref or out parameter `{0}' inside an " +
					      "anonymous method block", par.Name);
				return false;
			}

			if (!am.IsIterator && declared == referenced)
				return true;

			// Don't capture aruments when the probing is on
			if (!ec.IsInProbingMode) {
				ScopeInfo scope = declared.CreateScopeInfo ();
				variable = scope.AddParameter (par, pi.Index);
				type = variable.Type;
			}
			return true;
		}

		public override int GetHashCode ()
		{
			return Name.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			ParameterReference pr = obj as ParameterReference;
			if (pr == null)
				return false;

			return Name == pr.Name && referenced == pr.referenced;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			return Parameter.ExpressionTreeVariableReference ();
		}

		//
		// Notice that for ref/out parameters, the type exposed is not the
		// same type exposed externally.
		//
		// for "ref int a":
		//   externally we expose "int&"
		//   here we expose       "int".
		//
		// We record this in "is_ref".  This means that the type system can treat
		// the type as it is expected, but when we generate the code, we generate
		// the alternate kind of code.
		//
		public override Expression DoResolve (EmitContext ec)
		{
			if (!DoResolveBase (ec))
				return null;

			if (is_out && ec.DoFlowAnalysis &&
			    (!ec.OmitStructFlowAnalysis || !VariableInfo.TypeInfo.IsStruct) && !IsAssigned (ec, loc))
				return null;

			return this;
		}

		override public Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			if (!DoResolveBase (ec))
				return null;

			// HACK: parameters are not captured when probing is on
			if (!ec.IsInProbingMode)
				SetAssigned (ec);

			return this;
		}

		static public void EmitLdArg (ILGenerator ig, int x)
		{
			if (x <= 255){
				switch (x){
				case 0: ig.Emit (OpCodes.Ldarg_0); break;
				case 1: ig.Emit (OpCodes.Ldarg_1); break;
				case 2: ig.Emit (OpCodes.Ldarg_2); break;
				case 3: ig.Emit (OpCodes.Ldarg_3); break;
				default: ig.Emit (OpCodes.Ldarg_S, (byte) x); break;
				}
			} else
				ig.Emit (OpCodes.Ldarg, x);
		}
		
		public override string ToString ()
		{
			return "ParameterReference[" + Name + "]";
		}
	}
	
	/// <summary>
	///   Used for arguments to New(), Invocation()
	/// </summary>
	public class Argument {
		public enum AType : byte {
			Expression,
			Ref,
			Out,
			ArgList
		};

		public static readonly Argument[] Empty = new Argument [0];

		public readonly AType ArgType;
		public Expression Expr;
		
		public Argument (Expression expr, AType type)
		{
			this.Expr = expr;
			this.ArgType = type;
		}

		public Argument (Expression expr)
		{
			this.Expr = expr;
			this.ArgType = AType.Expression;
		}

		public Type Type {
			get {
				if (ArgType == AType.Ref || ArgType == AType.Out)
					return TypeManager.GetReferenceType (Expr.Type);
				else
					return Expr.Type;
			}
		}

		public Parameter.Modifier Modifier
		{
			get {
				switch (ArgType) {
					case AType.Out:
						return Parameter.Modifier.OUT;

					case AType.Ref:
						return Parameter.Modifier.REF;

					default:
						return Parameter.Modifier.NONE;
				}
			}
		}

		public string GetSignatureForError ()
		{
			if (Expr.eclass == ExprClass.MethodGroup)
				return Expr.ExprClassName;

			return Expr.GetSignatureForError ();
		}		

		public bool ResolveMethodGroup (EmitContext ec)
		{
			SimpleName sn = Expr as SimpleName;
			if (sn != null)
				Expr = sn.GetMethodGroup ();

			// FIXME: csc doesn't report any error if you try to use `ref' or
			//        `out' in a delegate creation expression.
			Expr = Expr.Resolve (ec, ResolveFlags.VariableOrValue | ResolveFlags.MethodGroup);
			if (Expr == null)
				return false;

			return true;
		}

		public bool Resolve (EmitContext ec, Location loc)
		{
			using (ec.With (EmitContext.Flags.DoFlowAnalysis, true)) {
				// Verify that the argument is readable
				if (ArgType != AType.Out)
					Expr = Expr.Resolve (ec);

				// Verify that the argument is writeable
				if (Expr != null && (ArgType == AType.Out || ArgType == AType.Ref))
					Expr = Expr.ResolveLValue (ec, EmptyExpression.OutAccess, loc);

				return Expr != null;
			}
		}

		public void Emit (EmitContext ec)
		{
			if (ArgType != AType.Ref && ArgType != AType.Out) {
				Expr.Emit (ec);
				return;
			}

			AddressOp mode = AddressOp.Store;
			if (ArgType == AType.Ref)
				mode |= AddressOp.Load;
				
			IMemoryLocation ml = (IMemoryLocation) Expr;
			ParameterReference pr = ml as ParameterReference;

			//
			// ParameterReferences might already be references, so we want
			// to pass just the value
			//
			if (pr != null && pr.IsRef)
				pr.EmitLoad (ec);
			else
				ml.AddressOf (ec, mode);
		}

		public Argument Clone (CloneContext clonectx)
		{
			return new Argument (Expr.Clone (clonectx), ArgType);
		}
	}

	/// <summary>
	///   Invocation of methods or delegates.
	/// </summary>
	public class Invocation : ExpressionStatement {
		protected ArrayList Arguments;
		Expression expr;
		protected MethodGroupExpr mg;
		bool arguments_resolved;
		
		//
		// arguments is an ArrayList, but we do not want to typecast,
		// as it might be null.
		//
		public Invocation (Expression expr, ArrayList arguments)
		{
			SimpleName sn = expr as SimpleName;
			if (sn != null)
				this.expr = sn.GetMethodGroup ();
			else
				this.expr = expr;
			
			Arguments = arguments;
			loc = expr.Location;
		}

		public Invocation (Expression expr, ArrayList arguments, bool arguments_resolved)
			: this (expr, arguments)
		{
			this.arguments_resolved = arguments_resolved;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (Arguments.Count + 3);
			if (mg.IsInstance)
				args.Add (new Argument (mg.InstanceExpression.CreateExpressionTree (ec)));
			else
				args.Add (new Argument (new NullConstant (loc).CreateExpressionTree (ec)));

			args.Add (new Argument (mg.CreateExpressionTree (ec)));
			foreach (Argument a in Arguments) {
				Expression e = a.Expr.CreateExpressionTree (ec);
				if (e != null)
					args.Add (new Argument (e));
			}

			return CreateExpressionFactoryCall ("Call", args);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			// Don't resolve already resolved expression
			if (eclass != ExprClass.Invalid)
				return this;
			
			Expression expr_resolved = expr.Resolve (ec, ResolveFlags.VariableOrValue | ResolveFlags.MethodGroup);
			if (expr_resolved == null)
				return null;

			mg = expr_resolved as MethodGroupExpr;
			if (mg == null) {
				Type expr_type = expr_resolved.Type;

				if (expr_type != null && TypeManager.IsDelegateType (expr_type)){
					return (new DelegateInvocation (
						expr_resolved, Arguments, loc)).Resolve (ec);
				}

				MemberExpr me = expr_resolved as MemberExpr;
				if (me == null) {
					expr_resolved.Error_UnexpectedKind (ResolveFlags.MethodGroup, loc);
					return null;
				}
				
				mg = ec.TypeContainer.LookupExtensionMethod (me.Type, me.Name);
				if (mg == null) {
					Report.Error (1955, loc, "The member `{0}' cannot be used as method or delegate",
						expr_resolved.GetSignatureForError ());
					return null;
				}

				((ExtensionMethodGroupExpr)mg).ExtensionExpression = me.InstanceExpression;
			}

			//
			// Next, evaluate all the expressions in the argument list
			//
			if (Arguments != null && !arguments_resolved) {
				for (int i = 0; i < Arguments.Count; ++i)
				{
					if (!((Argument)Arguments[i]).Resolve(ec, loc))
						return null;
				}
			}

			mg = DoResolveOverload (ec);
			if (mg == null)
				return null;

			MethodInfo method = (MethodInfo)mg;
			if (method != null) {
				type = TypeManager.TypeToCoreType (method.ReturnType);

				// TODO: this is a copy of mg.ResolveMemberAccess method
				Expression iexpr = mg.InstanceExpression;
				if (method.IsStatic) {
					if (iexpr == null ||
						iexpr is This || iexpr is EmptyExpression ||
						mg.IdenticalTypeName) {
						mg.InstanceExpression = null;
					} else {
						MemberExpr.error176 (loc, mg.GetSignatureForError ());
						return null;
					}
				}
			}

			if (type.IsPointer){
				if (!ec.InUnsafe){
					UnsafeError (loc);
					return null;
				}
			}
			
			//
			// Only base will allow this invocation to happen.
			//
			if (mg.IsBase && method.IsAbstract){
				Error_CannotCallAbstractBase (TypeManager.CSharpSignature (method));
				return null;
			}

			if (Arguments == null && method.DeclaringType == TypeManager.object_type && method.Name == "Finalize") {
				if (mg.IsBase)
					Report.Error (250, loc, "Do not directly call your base class Finalize method. It is called automatically from your destructor");
				else
					Report.Error (245, loc, "Destructors and object.Finalize cannot be called directly. Consider calling IDisposable.Dispose if available");
				return null;
			}

			if (IsSpecialMethodInvocation (method)) {
				return null;
			}
			
			if (mg.InstanceExpression != null)
				mg.InstanceExpression.CheckMarshalByRefAccess (ec);

			eclass = ExprClass.Value;
			return this;
		}

		protected virtual MethodGroupExpr DoResolveOverload (EmitContext ec)
		{
			return mg.OverloadResolve (ec, ref Arguments, false, loc);
		}

		bool IsSpecialMethodInvocation (MethodBase method)
		{
			if (!TypeManager.IsSpecialMethod (method))
				return false;

			Report.SymbolRelatedToPreviousError (method);
			Report.Error (571, loc, "`{0}': cannot explicitly call operator or accessor",
				TypeManager.CSharpSignature (method, true));
	
			return true;
		}

		/// <summary>
		///   Emits a list of resolved Arguments that are in the arguments
		///   ArrayList.
		/// 
		///   The MethodBase argument might be null if the
		///   emission of the arguments is known not to contain
		///   a `params' field (for example in constructors or other routines
		///   that keep their arguments in this structure)
		///   
		///   if `dup_args' is true, a copy of the arguments will be left
		///   on the stack. If `dup_args' is true, you can specify `this_arg'
		///   which will be duplicated before any other args. Only EmitCall
		///   should be using this interface.
		/// </summary>
		public static void EmitArguments (EmitContext ec, ArrayList arguments, bool dup_args, LocalTemporary this_arg)
		{
			if (arguments == null)
				return;

			int top = arguments.Count;
			LocalTemporary [] temps = null;
			
			if (dup_args && top != 0)
				temps = new LocalTemporary [top];

			int argument_index = 0;
			Argument a;
			for (int i = 0; i < top; i++) {
				a = (Argument) arguments [argument_index++];
				a.Emit (ec);
				if (dup_args) {
					ec.ig.Emit (OpCodes.Dup);
					(temps [i] = new LocalTemporary (a.Type)).Store (ec);
				}
			}
			
			if (dup_args) {
				if (this_arg != null)
					this_arg.Emit (ec);
				
				for (int i = 0; i < top; i ++) {
					temps [i].Emit (ec);
					temps [i].Release (ec);
				}
			}
		}

		static Type[] GetVarargsTypes (MethodBase mb, ArrayList arguments)
		{
			ParameterData pd = TypeManager.GetParameterData (mb);

			if (arguments == null)
				return new Type [0];

			Argument a = (Argument) arguments [pd.Count - 1];
			Arglist list = (Arglist) a.Expr;

			return list.ArgumentTypes;
		}

		/// <summary>
		/// This checks the ConditionalAttribute on the method 
		/// </summary>
		public static bool IsMethodExcluded (MethodBase method)
		{
			if (method.IsConstructor)
				return false;

			method = TypeManager.DropGenericMethodArguments (method);
			if (method.DeclaringType.Module == CodeGen.Module.Builder) {
				IMethodData md = TypeManager.GetMethod (method);
				if (md != null)
					return md.IsExcluded ();

				// For some methods (generated by delegate class) GetMethod returns null
				// because they are not included in builder_to_method table
				return false;
			}

			return AttributeTester.IsConditionalMethodExcluded (method);
		}

		/// <remarks>
		///   is_base tells whether we want to force the use of the `call'
		///   opcode instead of using callvirt.  Call is required to call
		///   a specific method, while callvirt will always use the most
		///   recent method in the vtable.
		///
		///   is_static tells whether this is an invocation on a static method
		///
		///   instance_expr is an expression that represents the instance
		///   it must be non-null if is_static is false.
		///
		///   method is the method to invoke.
		///
		///   Arguments is the list of arguments to pass to the method or constructor.
		/// </remarks>
		public static void EmitCall (EmitContext ec, bool is_base,
					     Expression instance_expr,
					     MethodBase method, ArrayList Arguments, Location loc)
		{
			EmitCall (ec, is_base, instance_expr, method, Arguments, loc, false, false);
		}
		
		// `dup_args' leaves an extra copy of the arguments on the stack
		// `omit_args' does not leave any arguments at all.
		// So, basically, you could make one call with `dup_args' set to true,
		// and then another with `omit_args' set to true, and the two calls
		// would have the same set of arguments. However, each argument would
		// only have been evaluated once.
		public static void EmitCall (EmitContext ec, bool is_base,
					     Expression instance_expr,
					     MethodBase method, ArrayList Arguments, Location loc,
		                             bool dup_args, bool omit_args)
		{
			ILGenerator ig = ec.ig;
			bool struct_call = false;
			bool this_call = false;
			LocalTemporary this_arg = null;

			Type decl_type = method.DeclaringType;

			if (!RootContext.StdLib) {
				// Replace any calls to the system's System.Array type with calls to
				// the newly created one.
				if (method == TypeManager.system_int_array_get_length)
					method = TypeManager.int_array_get_length;
				else if (method == TypeManager.system_int_array_get_rank)
					method = TypeManager.int_array_get_rank;
				else if (method == TypeManager.system_object_array_clone)
					method = TypeManager.object_array_clone;
				else if (method == TypeManager.system_int_array_get_length_int)
					method = TypeManager.int_array_get_length_int;
				else if (method == TypeManager.system_int_array_get_lower_bound_int)
					method = TypeManager.int_array_get_lower_bound_int;
				else if (method == TypeManager.system_int_array_get_upper_bound_int)
					method = TypeManager.int_array_get_upper_bound_int;
				else if (method == TypeManager.system_void_array_copyto_array_int)
					method = TypeManager.void_array_copyto_array_int;
			}

			if (!ec.IsInObsoleteScope) {
				//
				// This checks ObsoleteAttribute on the method and on the declaring type
				//
				ObsoleteAttribute oa = AttributeTester.GetMethodObsoleteAttribute (method);
				if (oa != null)
					AttributeTester.Report_ObsoleteMessage (oa, TypeManager.CSharpSignature (method), loc);

				oa = AttributeTester.GetObsoleteAttribute (method.DeclaringType);
				if (oa != null) {
					AttributeTester.Report_ObsoleteMessage (oa, method.DeclaringType.FullName, loc);
				}
			}

			if (IsMethodExcluded (method))
				return;
			
			bool is_static = method.IsStatic;
			if (!is_static){
				if (instance_expr == EmptyExpression.Null) {
					SimpleName.Error_ObjectRefRequired (ec, loc, TypeManager.CSharpSignature (method));
					return;
				}

				this_call = instance_expr is This;
				if (decl_type.IsValueType || (!this_call && instance_expr.Type.IsValueType))
					struct_call = true;

				//
				// If this is ourselves, push "this"
				//
				if (!omit_args) {
					Type t = null;
					Type iexpr_type = instance_expr.Type;

					//
					// Push the instance expression
					//
					if (TypeManager.IsValueType (iexpr_type)) {
						//
						// Special case: calls to a function declared in a 
						// reference-type with a value-type argument need
						// to have their value boxed.
						if (decl_type.IsValueType ||
						    TypeManager.IsGenericParameter (iexpr_type)) {
							//
							// If the expression implements IMemoryLocation, then
							// we can optimize and use AddressOf on the
							// return.
							//
							// If not we have to use some temporary storage for
							// it.
							if (instance_expr is IMemoryLocation) {
								((IMemoryLocation)instance_expr).
									AddressOf (ec, AddressOp.LoadStore);
							} else {
								LocalTemporary temp = new LocalTemporary (iexpr_type);
								instance_expr.Emit (ec);
								temp.Store (ec);
								temp.AddressOf (ec, AddressOp.Load);
							}

							// avoid the overhead of doing this all the time.
							if (dup_args)
								t = TypeManager.GetReferenceType (iexpr_type);
						} else {
							instance_expr.Emit (ec);
							ig.Emit (OpCodes.Box, instance_expr.Type);
							t = TypeManager.object_type;
						}
					} else {
						instance_expr.Emit (ec);
						t = instance_expr.Type;
					}

					if (dup_args) {
						ig.Emit (OpCodes.Dup);
						if (Arguments != null && Arguments.Count != 0) {
							this_arg = new LocalTemporary (t);
							this_arg.Store (ec);
						}
					}
				}
			}

			if (!omit_args)
				EmitArguments (ec, Arguments, dup_args, this_arg);

#if GMCS_SOURCE
			if ((instance_expr != null) && (instance_expr.Type.IsGenericParameter))
				ig.Emit (OpCodes.Constrained, instance_expr.Type);
#endif

			OpCode call_op;
			if (is_static || struct_call || is_base || (this_call && !method.IsVirtual))
				call_op = OpCodes.Call;
			else
				call_op = OpCodes.Callvirt;

			if ((method.CallingConvention & CallingConventions.VarArgs) != 0) {
				Type[] varargs_types = GetVarargsTypes (method, Arguments);
				ig.EmitCall (call_op, (MethodInfo) method, varargs_types);
				return;
			}

			//
			// If you have:
			// this.DoFoo ();
			// and DoFoo is not virtual, you can omit the callvirt,
			// because you don't need the null checking behavior.
			//
			if (method is MethodInfo)
				ig.Emit (call_op, (MethodInfo) method);
			else
				ig.Emit (call_op, (ConstructorInfo) method);
		}
		
		public override void Emit (EmitContext ec)
		{
			mg.EmitCall (ec, Arguments);
		}
		
		public override void EmitStatement (EmitContext ec)
		{
			Emit (ec);

			// 
			// Pop the return value if there is one
			//
			if (TypeManager.TypeToCoreType (type) != TypeManager.void_type)
				ec.ig.Emit (OpCodes.Pop);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Invocation target = (Invocation) t;

			if (Arguments != null) {
				target.Arguments = new ArrayList (Arguments.Count);
				foreach (Argument a in Arguments)
					target.Arguments.Add (a.Clone (clonectx));
			}

			target.expr = expr.Clone (clonectx);
		}
	}

	public class InvocationOrCast : ExpressionStatement
	{
		Expression expr;
		Expression argument;

		public InvocationOrCast (Expression expr, Expression argument)
		{
			this.expr = expr;
			this.argument = argument;
			this.loc = expr.Location;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			//
			// First try to resolve it as a cast.
			//
			TypeExpr te = expr.ResolveAsTypeTerminal (ec, true);
			if ((te != null) && (te.eclass == ExprClass.Type)) {
				Cast cast = new Cast (te, argument, loc);
				return cast.Resolve (ec);
			}

			//
			// This can either be a type or a delegate invocation.
			// Let's just resolve it and see what we'll get.
			//
			expr = expr.Resolve (ec, ResolveFlags.Type | ResolveFlags.VariableOrValue);
			if (expr == null)
				return null;

			//
			// Ok, so it's a Cast.
			//
			if (expr.eclass == ExprClass.Type) {
				Cast cast = new Cast (new TypeExpression (expr.Type, loc), argument, loc);
				return cast.Resolve (ec);
			}

			//
			// It's a delegate invocation.
			//
			if (!TypeManager.IsDelegateType (expr.Type)) {
				Error (149, "Method name expected");
				return null;
			}

			ArrayList args = new ArrayList ();
			args.Add (new Argument (argument, Argument.AType.Expression));
			DelegateInvocation invocation = new DelegateInvocation (expr, args, loc);
			return invocation.Resolve (ec);
		}

		public override ExpressionStatement ResolveStatement (EmitContext ec)
		{
			//
			// First try to resolve it as a cast.
			//
			TypeExpr te = expr.ResolveAsTypeTerminal (ec, true);
			if ((te != null) && (te.eclass == ExprClass.Type)) {
				Error_InvalidExpressionStatement ();
				return null;
			}

			//
			// This can either be a type or a delegate invocation.
			// Let's just resolve it and see what we'll get.
			//
			expr = expr.Resolve (ec, ResolveFlags.Type | ResolveFlags.VariableOrValue);
			if ((expr == null) || (expr.eclass == ExprClass.Type)) {
				Error_InvalidExpressionStatement ();
				return null;
			}

			//
			// It's a delegate invocation.
			//
			if (!TypeManager.IsDelegateType (expr.Type)) {
				Error (149, "Method name expected");
				return null;
			}

			ArrayList args = new ArrayList ();
			args.Add (new Argument (argument, Argument.AType.Expression));
			DelegateInvocation invocation = new DelegateInvocation (expr, args, loc);
			return invocation.ResolveStatement (ec);
		}

		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Cannot happen");
		}

		public override void EmitStatement (EmitContext ec)
		{
			throw new Exception ("Cannot happen");
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			InvocationOrCast target = (InvocationOrCast) t;

			target.expr = expr.Clone (clonectx);
			target.argument = argument.Clone (clonectx);
		}
	}

	//
	// This class is used to "disable" the code generation for the
	// temporary variable when initializing value types.
	//
	class EmptyAddressOf : EmptyExpression, IMemoryLocation {
		public void AddressOf (EmitContext ec, AddressOp Mode)
		{
			// nothing
		}
	}
	
	/// <summary>
	///    Implements the new expression 
	/// </summary>
	public class New : ExpressionStatement, IMemoryLocation {
		ArrayList Arguments;

		//
		// During bootstrap, it contains the RequestedType,
		// but if `type' is not null, it *might* contain a NewDelegate
		// (because of field multi-initialization)
		//
		public Expression RequestedType;

		MethodGroupExpr method;

		//
		// If set, the new expression is for a value_target, and
		// we will not leave anything on the stack.
		//
		protected Expression value_target;
		protected bool value_target_set;
		bool is_type_parameter = false;
		
		public New (Expression requested_type, ArrayList arguments, Location l)
		{
			RequestedType = requested_type;
			Arguments = arguments;
			loc = l;
		}

		public bool SetTargetVariable (Expression value)
		{
			value_target = value;
			value_target_set = true;
			if (!(value_target is IMemoryLocation)){
				Error_UnexpectedKind (null, "variable", loc);
				return false;
			}
			return true;
		}

		//
		// This function is used to disable the following code sequence for
		// value type initialization:
		//
		// AddressOf (temporary)
		// Construct/Init
		// LoadTemporary
		//
		// Instead the provide will have provided us with the address on the
		// stack to store the results.
		//
		static Expression MyEmptyExpression;
		
		public void DisableTemporaryValueType ()
		{
			if (MyEmptyExpression == null)
				MyEmptyExpression = new EmptyAddressOf ();

			//
			// To enable this, look into:
			// test-34 and test-89 and self bootstrapping.
			//
			// For instance, we can avoid a copy by using `newobj'
			// instead of Call + Push-temp on value types.
//			value_target = MyEmptyExpression;
		}


		/// <summary>
		/// Converts complex core type syntax like 'new int ()' to simple constant
		/// </summary>
		public static Constant Constantify (Type t)
		{
			if (t == TypeManager.int32_type)
				return new IntConstant (0, Location.Null);
			if (t == TypeManager.uint32_type)
				return new UIntConstant (0, Location.Null);
			if (t == TypeManager.int64_type)
				return new LongConstant (0, Location.Null);
			if (t == TypeManager.uint64_type)
				return new ULongConstant (0, Location.Null);
			if (t == TypeManager.float_type)
				return new FloatConstant (0, Location.Null);
			if (t == TypeManager.double_type)
				return new DoubleConstant (0, Location.Null);
			if (t == TypeManager.short_type)
				return new ShortConstant (0, Location.Null);
			if (t == TypeManager.ushort_type)
				return new UShortConstant (0, Location.Null);
			if (t == TypeManager.sbyte_type)
				return new SByteConstant (0, Location.Null);
			if (t == TypeManager.byte_type)
				return new ByteConstant (0, Location.Null);
			if (t == TypeManager.char_type)
				return new CharConstant ('\0', Location.Null);
			if (t == TypeManager.bool_type)
				return new BoolConstant (false, Location.Null);
			if (t == TypeManager.decimal_type)
				return new DecimalConstant (0, Location.Null);
			if (TypeManager.IsEnumType (t))
				return new EnumConstant (Constantify (TypeManager.EnumToUnderlying (t)), t);

			return null;
		}

		//
		// Checks whether the type is an interface that has the
		// [ComImport, CoClass] attributes and must be treated
		// specially
		//
		public Expression CheckComImport (EmitContext ec)
		{
			if (!type.IsInterface)
				return null;

			//
			// Turn the call into:
			// (the-interface-stated) (new class-referenced-in-coclassattribute ())
			//
			Type real_class = AttributeTester.GetCoClassAttribute (type);
			if (real_class == null)
				return null;

			New proxy = new New (new TypeExpression (real_class, loc), Arguments, loc);
			Cast cast = new Cast (new TypeExpression (type, loc), proxy, loc);
			return cast.Resolve (ec);
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			//
			// The New DoResolve might be called twice when initializing field
			// expressions (see EmitFieldInitializers, the call to
			// GetInitializerExpression will perform a resolve on the expression,
			// and later the assign will trigger another resolution
			//
			// This leads to bugs (#37014)
			//
			if (type != null){
				if (RequestedType is NewDelegate)
					return RequestedType;
				return this;
			}

			TypeExpr texpr = RequestedType.ResolveAsTypeTerminal (ec, false);
			if (texpr == null)
				return null;

			type = texpr.Type;

			if (type == TypeManager.void_type) {
				Error_VoidInvalidInTheContext (loc);
				return null;
			}

			if (type.IsPointer) {
				Report.Error (1919, loc, "Unsafe type `{0}' cannot be used in an object creation expression",
					TypeManager.CSharpName (type));
				return null;
			}

			if (Arguments == null) {
				Expression c = Constantify (type);
				if (c != null)
					return c;
			}

			if (TypeManager.IsDelegateType (type)) {
				RequestedType = (new NewDelegate (type, Arguments, loc)).Resolve (ec);
				if (RequestedType != null)
					if (!(RequestedType is DelegateCreation))
						throw new Exception ("NewDelegate.Resolve returned a non NewDelegate: " + RequestedType.GetType ());
				return RequestedType;
			}

#if GMCS_SOURCE
			if (type.IsGenericParameter) {
				GenericConstraints gc = TypeManager.GetTypeParameterConstraints (type);

				if ((gc == null) || (!gc.HasConstructorConstraint && !gc.IsValueType)) {
					Error (304, String.Format (
						       "Cannot create an instance of the " +
						       "variable type '{0}' because it " +
						       "doesn't have the new() constraint",
						       type));
					return null;
				}

				if ((Arguments != null) && (Arguments.Count != 0)) {
					Error (417, String.Format (
						       "`{0}': cannot provide arguments " +
						       "when creating an instance of a " +
						       "variable type.", type));
					return null;
				}

				is_type_parameter = true;
				eclass = ExprClass.Value;
				return this;
			}
#endif

			if (type.IsAbstract && type.IsSealed) {
				Report.SymbolRelatedToPreviousError (type);
				Report.Error (712, loc, "Cannot create an instance of the static class `{0}'", TypeManager.CSharpName (type));
				return null;
			}

			if (type.IsInterface || type.IsAbstract){
				if (!TypeManager.IsGenericType (type)) {
					RequestedType = CheckComImport (ec);
					if (RequestedType != null)
						return RequestedType;
				}
				
				Report.SymbolRelatedToPreviousError (type);
				Report.Error (144, loc, "Cannot create an instance of the abstract class or interface `{0}'", TypeManager.CSharpName (type));
				return null;
			}

			bool is_struct = type.IsValueType;
			eclass = ExprClass.Value;

			//
			// SRE returns a match for .ctor () on structs (the object constructor), 
			// so we have to manually ignore it.
			//
			if (is_struct && Arguments == null)
				return this;

			// For member-lookup, treat 'new Foo (bar)' as call to 'foo.ctor (bar)', where 'foo' is of type 'Foo'.
			Expression ml = MemberLookupFinal (ec, type, type, ".ctor",
				MemberTypes.Constructor, AllBindingFlags | BindingFlags.DeclaredOnly, loc);

			if (Arguments != null){
				foreach (Argument a in Arguments){
					if (!a.Resolve (ec, loc))
						return null;
				}
			}

			if (ml == null)
				return null;

			method = ml as MethodGroupExpr;
			if (method == null) {
				ml.Error_UnexpectedKind (ec.DeclContainer, "method group", loc);
				return null;
			}

			method = method.OverloadResolve (ec, ref Arguments, false, loc);
			if (method == null)
				return null;

			return this;
		}

		bool DoEmitTypeParameter (EmitContext ec)
		{
#if GMCS_SOURCE
			ILGenerator ig = ec.ig;
//			IMemoryLocation ml;

			MethodInfo ci = TypeManager.activator_create_instance.MakeGenericMethod (
				new Type [] { type });

			GenericConstraints gc = TypeManager.GetTypeParameterConstraints (type);
			if (gc.HasReferenceTypeConstraint || gc.HasClassConstraint) {
				ig.Emit (OpCodes.Call, ci);
				return true;
			}

			// Allow DoEmit() to be called multiple times.
			// We need to create a new LocalTemporary each time since
			// you can't share LocalBuilders among ILGeneators.
			LocalTemporary temp = new LocalTemporary (type);

			Label label_activator = ig.DefineLabel ();
			Label label_end = ig.DefineLabel ();

			temp.AddressOf (ec, AddressOp.Store);
			ig.Emit (OpCodes.Initobj, type);

			temp.Emit (ec);
			ig.Emit (OpCodes.Box, type);
			ig.Emit (OpCodes.Brfalse, label_activator);

			temp.AddressOf (ec, AddressOp.Store);
			ig.Emit (OpCodes.Initobj, type);
			temp.Emit (ec);
			ig.Emit (OpCodes.Br, label_end);

			ig.MarkLabel (label_activator);

			ig.Emit (OpCodes.Call, ci);
			ig.MarkLabel (label_end);
			return true;
#else
			throw new InternalErrorException ();
#endif
		}

		//
		// This DoEmit can be invoked in two contexts:
		//    * As a mechanism that will leave a value on the stack (new object)
		//    * As one that wont (init struct)
		//
		// You can control whether a value is required on the stack by passing
		// need_value_on_stack.  The code *might* leave a value on the stack
		// so it must be popped manually
		//
		// If we are dealing with a ValueType, we have a few
		// situations to deal with:
		//
		//    * The target is a ValueType, and we have been provided
		//      the instance (this is easy, we are being assigned).
		//
		//    * The target of New is being passed as an argument,
		//      to a boxing operation or a function that takes a
		//      ValueType.
		//
		//      In this case, we need to create a temporary variable
		//      that is the argument of New.
		//
		// Returns whether a value is left on the stack
		//
		bool DoEmit (EmitContext ec, bool need_value_on_stack)
		{
			bool is_value_type = TypeManager.IsValueType (type);
			ILGenerator ig = ec.ig;

			if (is_value_type){
				IMemoryLocation ml;

				// Allow DoEmit() to be called multiple times.
				// We need to create a new LocalTemporary each time since
				// you can't share LocalBuilders among ILGeneators.
				if (!value_target_set)
					value_target = new LocalTemporary (type);

				ml = (IMemoryLocation) value_target;
				ml.AddressOf (ec, AddressOp.Store);
			}

			if (method != null)
				method.EmitArguments (ec, Arguments);

			if (is_value_type){
				if (method == null)
					ig.Emit (OpCodes.Initobj, type);
				else
					ig.Emit (OpCodes.Call, (ConstructorInfo) method);
                                if (need_value_on_stack){
                                        value_target.Emit (ec);
                                        return true;
                                }
                                return false;
			} else {
				ig.Emit (OpCodes.Newobj, (ConstructorInfo) method);
				return true;
			}
		}

		public override void Emit (EmitContext ec)
		{
			if (is_type_parameter)
				DoEmitTypeParameter (ec);
			else
				DoEmit (ec, true);
		}
		
		public override void EmitStatement (EmitContext ec)
		{
			bool value_on_stack;

			if (is_type_parameter)
				value_on_stack = DoEmitTypeParameter (ec);
			else
				value_on_stack = DoEmit (ec, false);

			if (value_on_stack)
				ec.ig.Emit (OpCodes.Pop);

		}

		public virtual bool HasInitializer {
			get {
				return false;
			}
		}

		public void AddressOf (EmitContext ec, AddressOp Mode)
		{
			if (is_type_parameter) {
				LocalTemporary temp = new LocalTemporary (type);
				DoEmitTypeParameter (ec);
				temp.Store (ec);
				temp.AddressOf (ec, Mode);
				return;
			}

			if (!type.IsValueType){
				//
				// We throw an exception.  So far, I believe we only need to support
				// value types:
				// foreach (int j in new StructType ())
				// see bug 42390
				//
				throw new Exception ("AddressOf should not be used for classes");
			}

			if (!value_target_set)
				value_target = new LocalTemporary (type);
			IMemoryLocation ml = (IMemoryLocation) value_target;

			ml.AddressOf (ec, AddressOp.Store);
			if (method == null) {
				ec.ig.Emit (OpCodes.Initobj, type);
			} else {
				method.EmitArguments (ec, Arguments);
				ec.ig.Emit (OpCodes.Call, (ConstructorInfo) method);
			}
			
			((IMemoryLocation) value_target).AddressOf (ec, Mode);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			New target = (New) t;

			target.RequestedType = RequestedType.Clone (clonectx);
			if (Arguments != null){
				target.Arguments = new ArrayList ();
				foreach (Argument a in Arguments){
					target.Arguments.Add (a.Clone (clonectx));
				}
			}
		}
	}

	/// <summary>
	///   14.5.10.2: Represents an array creation expression.
	/// </summary>
	///
	/// <remarks>
	///   There are two possible scenarios here: one is an array creation
	///   expression that specifies the dimensions and optionally the
	///   initialization data and the other which does not need dimensions
	///   specified but where initialization data is mandatory.
	/// </remarks>
	public class ArrayCreation : Expression {
		Expression requested_base_type;
		ArrayList initializers;

		//
		// The list of Argument types.
		// This is used to construct the `newarray' or constructor signature
		//
		protected ArrayList arguments;
		
		protected Type array_element_type;
		bool expect_initializers = false;
		int num_arguments = 0;
		protected int dimensions;
		protected readonly string rank;

		protected ArrayList array_data;

		IDictionary bounds;

		// The number of constants in array initializers
		int const_initializers_count;
		bool only_constant_initializers;
		
		public ArrayCreation (Expression requested_base_type, ArrayList exprs, string rank, ArrayList initializers, Location l)
		{
			this.requested_base_type = requested_base_type;
			this.initializers = initializers;
			this.rank = rank;
			loc = l;

			arguments = new ArrayList ();

			foreach (Expression e in exprs) {
				arguments.Add (new Argument (e, Argument.AType.Expression));
				num_arguments++;
			}
		}

		public ArrayCreation (Expression requested_base_type, string rank, ArrayList initializers, Location l)
		{
			this.requested_base_type = requested_base_type;
			this.initializers = initializers;
			this.rank = rank;
			loc = l;

			//this.rank = rank.Substring (0, rank.LastIndexOf ('['));
			//
			//string tmp = rank.Substring (rank.LastIndexOf ('['));
			//
			//dimensions = tmp.Length - 1;
			expect_initializers = true;
		}

		public Expression FormArrayType (Expression base_type, int idx_count, string rank)
		{
			StringBuilder sb = new StringBuilder (rank);
			
			sb.Append ("[");
			for (int i = 1; i < idx_count; i++)
				sb.Append (",");
			
			sb.Append ("]");

			return new ComposedCast (base_type, sb.ToString (), loc);
		}

		void Error_IncorrectArrayInitializer ()
		{
			Error (178, "Invalid rank specifier: expected `,' or `]'");
		}

		protected override void Error_NegativeArrayIndex (Location loc)
		{
			Report.Error (248, loc, "Cannot create an array with a negative size");
		}
		
		bool CheckIndices (EmitContext ec, ArrayList probe, int idx, bool specified_dims)
		{
			if (specified_dims) { 
				Argument a = (Argument) arguments [idx];

				if (!a.Resolve (ec, loc))
					return false;

				Constant c = a.Expr as Constant;
				if (c != null) {
					c = c.ImplicitConversionRequired (TypeManager.int32_type, a.Expr.Location);
				}

				if (c == null) {
					Report.Error (150, a.Expr.Location, "A constant value is expected");
					return false;
				}

				int value = (int) c.GetValue ();
				
				if (value != probe.Count) {
					Error_IncorrectArrayInitializer ();
					return false;
				}
				
				bounds [idx] = value;
			}

			int child_bounds = -1;
			only_constant_initializers = true;
			for (int i = 0; i < probe.Count; ++i) {
				object o = probe [i];
				if (o is ArrayList) {
					ArrayList sub_probe = o as ArrayList;
					int current_bounds = sub_probe.Count;
					
					if (child_bounds == -1) 
						child_bounds = current_bounds;

					else if (child_bounds != current_bounds){
						Error_IncorrectArrayInitializer ();
						return false;
					}
					if (idx + 1 >= dimensions){
						Error (623, "Array initializers can only be used in a variable or field initializer. Try using a new expression instead");
						return false;
					}
					
					bool ret = CheckIndices (ec, sub_probe, idx + 1, specified_dims);
					if (!ret)
						return false;
				} else {
					if (child_bounds != -1){
						Error_IncorrectArrayInitializer ();
						return false;
					}
					
					Expression element = ResolveArrayElement (ec, (Expression) o);
					if (element == null)
						continue;

					// Initializers with the default values can be ignored
					Constant c = element as Constant;
					if (c != null) {
						if (c.IsDefaultInitializer (array_element_type)) {
							element = null;
						}
						else {
							++const_initializers_count;
						}
					} else {
						only_constant_initializers = false;
					}
					
					array_data.Add (element);
				}
			}

			return true;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			if (dimensions != 1) {
				Report.Error (838, loc, "An expression tree cannot contain a multidimensional array initializer");
				return null;
			}

			ArrayList args = new ArrayList (array_data == null ? 1 : array_data.Count + 1);
			args.Add (new Argument (new TypeOf (new TypeExpression (array_element_type, loc), loc)));
			if (array_data != null) {
				foreach (Expression e in array_data)
					args.Add (new Argument (e.CreateExpressionTree (ec)));
			}

			return CreateExpressionFactoryCall ("NewArrayInit", args);
		}		
		
		public void UpdateIndices ()
		{
			int i = 0;
			for (ArrayList probe = initializers; probe != null;) {
				if (probe.Count > 0 && probe [0] is ArrayList) {
					Expression e = new IntConstant (probe.Count, Location.Null);
					arguments.Add (new Argument (e, Argument.AType.Expression));

					bounds [i++] =  probe.Count;
					
					probe = (ArrayList) probe [0];
					
				} else {
					Expression e = new IntConstant (probe.Count, Location.Null);
					arguments.Add (new Argument (e, Argument.AType.Expression));

					bounds [i++] = probe.Count;
					return;
				}
			}

		}

		protected virtual Expression ResolveArrayElement (EmitContext ec, Expression element)
		{
			element = element.Resolve (ec);
			if (element == null)
				return null;

			return Convert.ImplicitConversionRequired (
				ec, element, array_element_type, loc);
		}

		protected bool ResolveInitializers (EmitContext ec)
		{
			if (initializers == null) {
				return !expect_initializers;
			}
						
			//
			// We use this to store all the date values in the order in which we
			// will need to store them in the byte blob later
			//
			array_data = new ArrayList ();
			bounds = new System.Collections.Specialized.HybridDictionary ();
			
			if (arguments != null)
				return CheckIndices (ec, initializers, 0, true);

			arguments = new ArrayList ();

			if (!CheckIndices (ec, initializers, 0, false))
				return false;
				
			UpdateIndices ();
				
			return true;
		}

		//
		// Resolved the type of the array
		//
		bool ResolveArrayType (EmitContext ec)
		{
			if (requested_base_type == null) {
				Report.Error (622, loc, "Can only use array initializer expressions to assign to array types. Try using a new expression instead");
				return false;
			}
			
			StringBuilder array_qualifier = new StringBuilder (rank);

			//
			// `In the first form allocates an array instace of the type that results
			// from deleting each of the individual expression from the expression list'
			//
			if (num_arguments > 0) {
				array_qualifier.Append ("[");
				for (int i = num_arguments-1; i > 0; i--)
					array_qualifier.Append (",");
				array_qualifier.Append ("]");
			}

			//
			// Lookup the type
			//
			TypeExpr array_type_expr;
			array_type_expr = new ComposedCast (requested_base_type, array_qualifier.ToString (), loc);
			array_type_expr = array_type_expr.ResolveAsTypeTerminal (ec, false);
			if (array_type_expr == null)
				return false;

			type = array_type_expr.Type;
			array_element_type = TypeManager.GetElementType (type);
			dimensions = type.GetArrayRank ();

			return true;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (type != null)
				return this;

			if (!ResolveArrayType (ec))
				return null;
			
			if ((array_element_type.Attributes & Class.StaticClassAttribute) == Class.StaticClassAttribute) {
				Report.Error (719, loc, "`{0}': array elements cannot be of static type",
					TypeManager.CSharpName (array_element_type));
			}

			//
			// First step is to validate the initializers and fill
			// in any missing bits
			//
			if (!ResolveInitializers (ec))
				return null;

			if (arguments.Count != dimensions) {
				Error_IncorrectArrayInitializer ();
			}

			foreach (Argument a in arguments){
				if (!a.Resolve (ec, loc))
					continue;

				a.Expr = ConvertExpressionToArrayIndex (ec, a.Expr);
			}
							
			eclass = ExprClass.Value;
			return this;
		}

		MethodInfo GetArrayMethod (int arguments)
		{
			ModuleBuilder mb = CodeGen.Module.Builder;

			Type[] arg_types = new Type[arguments];
			for (int i = 0; i < arguments; i++)
				arg_types[i] = TypeManager.int32_type;

			MethodInfo mi = mb.GetArrayMethod (type, ".ctor", CallingConventions.HasThis, null,
							arg_types);

			if (mi == null) {
				Report.Error (-6, "New invocation: Can not find a constructor for " +
						  "this argument list");
				return null;
			}

			return mi; 
		}

		byte [] MakeByteBlob ()
		{
			int factor;
			byte [] data;
			byte [] element;
			int count = array_data.Count;

			if (array_element_type.IsEnum)
				array_element_type = TypeManager.EnumToUnderlying (array_element_type);
			
			factor = GetTypeSize (array_element_type);
			if (factor == 0)
				throw new Exception ("unrecognized type in MakeByteBlob: " + array_element_type);

			data = new byte [(count * factor + 3) & ~3];
			int idx = 0;

			for (int i = 0; i < count; ++i) {
				object v = array_data [i];

				if (v is EnumConstant)
					v = ((EnumConstant) v).Child;
				
				if (v is Constant && !(v is StringConstant))
					v = ((Constant) v).GetValue ();
				else {
					idx += factor;
					continue;
				}
				
				if (array_element_type == TypeManager.int64_type){
					if (!(v is Expression)){
						long val = (long) v;
						
						for (int j = 0; j < factor; ++j) {
							data [idx + j] = (byte) (val & 0xFF);
							val = (val >> 8);
						}
					}
				} else if (array_element_type == TypeManager.uint64_type){
					if (!(v is Expression)){
						ulong val = (ulong) v;

						for (int j = 0; j < factor; ++j) {
							data [idx + j] = (byte) (val & 0xFF);
							val = (val >> 8);
						}
					}
				} else if (array_element_type == TypeManager.float_type) {
					if (!(v is Expression)){
						element = BitConverter.GetBytes ((float) v);
							
						for (int j = 0; j < factor; ++j)
							data [idx + j] = element [j];
						if (!BitConverter.IsLittleEndian)
							System.Array.Reverse (data, idx, 4);
					}
				} else if (array_element_type == TypeManager.double_type) {
					if (!(v is Expression)){
						element = BitConverter.GetBytes ((double) v);

						for (int j = 0; j < factor; ++j)
							data [idx + j] = element [j];

						// FIXME: Handle the ARM float format.
						if (!BitConverter.IsLittleEndian)
							System.Array.Reverse (data, idx, 8);
					}
				} else if (array_element_type == TypeManager.char_type){
					if (!(v is Expression)){
						int val = (int) ((char) v);
						
						data [idx] = (byte) (val & 0xff);
						data [idx+1] = (byte) (val >> 8);
					}
				} else if (array_element_type == TypeManager.short_type){
					if (!(v is Expression)){
						int val = (int) ((short) v);
					
						data [idx] = (byte) (val & 0xff);
						data [idx+1] = (byte) (val >> 8);
					}
				} else if (array_element_type == TypeManager.ushort_type){
					if (!(v is Expression)){
						int val = (int) ((ushort) v);
					
						data [idx] = (byte) (val & 0xff);
						data [idx+1] = (byte) (val >> 8);
					}
				} else if (array_element_type == TypeManager.int32_type) {
					if (!(v is Expression)){
						int val = (int) v;
					
						data [idx]   = (byte) (val & 0xff);
						data [idx+1] = (byte) ((val >> 8) & 0xff);
						data [idx+2] = (byte) ((val >> 16) & 0xff);
						data [idx+3] = (byte) (val >> 24);
					}
				} else if (array_element_type == TypeManager.uint32_type) {
					if (!(v is Expression)){
						uint val = (uint) v;
					
						data [idx]   = (byte) (val & 0xff);
						data [idx+1] = (byte) ((val >> 8) & 0xff);
						data [idx+2] = (byte) ((val >> 16) & 0xff);
						data [idx+3] = (byte) (val >> 24);
					}
				} else if (array_element_type == TypeManager.sbyte_type) {
					if (!(v is Expression)){
						sbyte val = (sbyte) v;
						data [idx] = (byte) val;
					}
				} else if (array_element_type == TypeManager.byte_type) {
					if (!(v is Expression)){
						byte val = (byte) v;
						data [idx] = (byte) val;
					}
				} else if (array_element_type == TypeManager.bool_type) {
					if (!(v is Expression)){
						bool val = (bool) v;
						data [idx] = (byte) (val ? 1 : 0);
					}
				} else if (array_element_type == TypeManager.decimal_type){
					if (!(v is Expression)){
						int [] bits = Decimal.GetBits ((decimal) v);
						int p = idx;

						// FIXME: For some reason, this doesn't work on the MS runtime.
						int [] nbits = new int [4];
						nbits [0] = bits [3];
						nbits [1] = bits [2];
						nbits [2] = bits [0];
						nbits [3] = bits [1];
						
						for (int j = 0; j < 4; j++){
							data [p++] = (byte) (nbits [j] & 0xff);
							data [p++] = (byte) ((nbits [j] >> 8) & 0xff);
							data [p++] = (byte) ((nbits [j] >> 16) & 0xff);
							data [p++] = (byte) (nbits [j] >> 24);
						}
					}
				} else
					throw new Exception ("Unrecognized type in MakeByteBlob: " + array_element_type);

                                idx += factor;
			}

			return data;
		}

		//
		// Emits the initializers for the array
		//
		void EmitStaticInitializers (EmitContext ec)
		{
			//
			// First, the static data
			//
			FieldBuilder fb;
			ILGenerator ig = ec.ig;
			
			byte [] data = MakeByteBlob ();

			fb = RootContext.MakeStaticData (data);

			ig.Emit (OpCodes.Dup);
			ig.Emit (OpCodes.Ldtoken, fb);
			ig.Emit (OpCodes.Call,
				 TypeManager.void_initializearray_array_fieldhandle);
		}

		//
		// Emits pieces of the array that can not be computed at compile
		// time (variables and string locations).
		//
		// This always expect the top value on the stack to be the array
		//
		void EmitDynamicInitializers (EmitContext ec, bool emitConstants)
		{
			ILGenerator ig = ec.ig;
			int dims = bounds.Count;
			int [] current_pos = new int [dims];

			MethodInfo set = null;

			if (dims != 1){
				Type [] args = new Type [dims + 1];

				for (int j = 0; j < dims; j++)
					args [j] = TypeManager.int32_type;
				args [dims] = array_element_type;
				
				set = CodeGen.Module.Builder.GetArrayMethod (
					type, "Set",
					CallingConventions.HasThis | CallingConventions.Standard,
					TypeManager.void_type, args);
			}

			for (int i = 0; i < array_data.Count; i++){

				Expression e = (Expression)array_data [i];

				// Constant can be initialized via StaticInitializer
				if (e != null && !(!emitConstants && e is Constant)) {
					Type etype = e.Type;

					ig.Emit (OpCodes.Dup);

					for (int idx = 0; idx < dims; idx++) 
						IntConstant.EmitInt (ig, current_pos [idx]);

					//
					// If we are dealing with a struct, get the
					// address of it, so we can store it.
					//
					if ((dims == 1) && etype.IsValueType &&
					    (!TypeManager.IsBuiltinOrEnum (etype) ||
					     etype == TypeManager.decimal_type)) {
						if (e is New){
							New n = (New) e;

							//
							// Let new know that we are providing
							// the address where to store the results
							//
							n.DisableTemporaryValueType ();
						}

						ig.Emit (OpCodes.Ldelema, etype);
					}

					e.Emit (ec);

					if (dims == 1) {
						bool is_stobj, has_type_arg;
						OpCode op = ArrayAccess.GetStoreOpcode (etype, out is_stobj, out has_type_arg);
						if (is_stobj)
							ig.Emit (OpCodes.Stobj, etype);
						else if (has_type_arg)
							ig.Emit (op, etype);
						else
							ig.Emit (op);
					} else 
						ig.Emit (OpCodes.Call, set);

				}
				
				//
				// Advance counter
				//
				for (int j = dims - 1; j >= 0; j--){
					current_pos [j]++;
					if (current_pos [j] < (int) bounds [j])
						break;
					current_pos [j] = 0;
				}
			}
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			foreach (Argument a in arguments)
				a.Emit (ec);

			if (arguments.Count == 1)
				ig.Emit (OpCodes.Newarr, array_element_type);
			else {
				ig.Emit (OpCodes.Newobj, GetArrayMethod (arguments.Count));
			}
			
			if (initializers == null)
				return;

			// Emit static initializer for arrays which have contain more than 4 items and
			// the static initializer will initialize at least 25% of array values.
			// NOTE: const_initializers_count does not contain default constant values.
			if (const_initializers_count >= 4 && const_initializers_count * 4 > (array_data.Count) &&
				TypeManager.IsPrimitiveType (array_element_type)) {
				EmitStaticInitializers (ec);

				if (!only_constant_initializers)
					EmitDynamicInitializers (ec, false);
			} else {
				EmitDynamicInitializers (ec, true);
			}				
		}

		public override bool GetAttributableValue (Type value_type, out object value)
		{
			if (arguments.Count != 1) {
				// Report.Error (-211, Location, "attribute can not encode multi-dimensional arrays");
				return base.GetAttributableValue (null, out value);
			}

			if (array_data == null) {
				Constant c = (Constant)((Argument)arguments [0]).Expr;
				if (c.IsDefaultValue) {
					value = Array.CreateInstance (array_element_type, 0);
					return true;
				}
				// Report.Error (-212, Location, "array should be initialized when passing it to an attribute");
				return base.GetAttributableValue (null, out value);
			}
			
			Array ret = Array.CreateInstance (array_element_type, array_data.Count);
			object element_value;
			for (int i = 0; i < ret.Length; ++i)
			{
				Expression e = (Expression)array_data [i];

				// Is null when an initializer is optimized (value == predefined value)
				if (e == null) 
					continue;

				if (!e.GetAttributableValue (array_element_type, out element_value)) {
					value = null;
					return false;
				}
				ret.SetValue (element_value, i);
			}
			value = ret;
			return true;
		}
		
		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			ArrayCreation target = (ArrayCreation) t;

			if (requested_base_type != null)
				target.requested_base_type = requested_base_type.Clone (clonectx);

			if (arguments != null){
				target.arguments = new ArrayList (arguments.Count);
				foreach (Argument a in arguments)
					target.arguments.Add (a.Clone (clonectx));
			}

			if (initializers != null){
				target.initializers = new ArrayList (initializers.Count);
				foreach (Expression initializer in initializers)
					target.initializers.Add (initializer.Clone (clonectx));
			}
		}
	}
	
	//
	// Represents an implicitly typed array epxression
	//
	public class ImplicitlyTypedArrayCreation : ArrayCreation
	{
		public ImplicitlyTypedArrayCreation (string rank, ArrayList initializers, Location loc)
			: base (null, rank, initializers, loc)
		{
			if (RootContext.Version <= LanguageVersion.ISO_2)
				Report.FeatureIsNotAvailable (loc, "implicitly typed arrays");
				
			if (rank.Length > 2) {
				while (rank [++dimensions] == ',');
			} else {
				dimensions = 1;
			}
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (type != null)
				return this;

			if (!ResolveInitializers (ec))
				return null;

			if (array_element_type == null || array_element_type == TypeManager.null_type ||
				array_element_type == TypeManager.void_type || array_element_type == TypeManager.anonymous_method_type ||
				arguments.Count != dimensions) {
				Report.Error (826, loc, "The type of an implicitly typed array cannot be inferred from the initializer. Try specifying array type explicitly");
				return null;
			}

			//
			// At this point we found common base type for all initializer elements
			// but we have to be sure that all static initializer elements are of
			// same type
			//
			UnifyInitializerElement (ec);

			type = TypeManager.GetConstructedType (array_element_type, rank);
			eclass = ExprClass.Value;
			return this;
		}

		//
		// Converts static initializer only
		//
		void UnifyInitializerElement (EmitContext ec)
		{
			for (int i = 0; i < array_data.Count; ++i) {
				Expression e = (Expression)array_data[i];
				if (e != null)
					array_data [i] = Convert.ImplicitConversionStandard (ec, e, array_element_type, Location.Null);
			}
		}

		protected override Expression ResolveArrayElement (EmitContext ec, Expression element)
		{
			element = element.Resolve (ec);
			if (element == null)
				return null;
			
			if (array_element_type == null) {
				array_element_type = element.Type;
				return element;
			}

			if (Convert.ImplicitStandardConversionExists (element, array_element_type)) {
				return element;
			}

			if (Convert.ImplicitStandardConversionExists (new TypeExpression (array_element_type, loc), element.Type)) {
				array_element_type = element.Type;
				return element;
			}

			element.Error_ValueCannotBeConverted (ec, element.Location, array_element_type, false);
			return element;
		}
	}	
	
	public sealed class CompilerGeneratedThis : This
	{
		public static This Instance = new CompilerGeneratedThis ();

		private CompilerGeneratedThis ()
			: base (Location.Null)
		{
		}

		public override Expression DoResolve (EmitContext ec)
		{
			eclass = ExprClass.Variable;
			type = ec.ContainerType;
			variable = new SimpleThis (type);
			return this;
		}
	}
	
	/// <summary>
	///   Represents the `this' construct
	/// </summary>

	public class This : VariableReference, IVariable
	{
		Block block;
		VariableInfo variable_info;
		protected Variable variable;
		bool is_struct;

		public This (Block block, Location loc)
		{
			this.loc = loc;
			this.block = block;
		}

		public This (Location loc)
		{
			this.loc = loc;
		}

		public VariableInfo VariableInfo {
			get { return variable_info; }
		}

		public bool VerifyFixed ()
		{
			return !TypeManager.IsValueType (Type);
		}

		public override bool IsRef {
			get { return is_struct; }
		}

		public override Variable Variable {
			get { return variable; }
		}

		public bool ResolveBase (EmitContext ec)
		{
			eclass = ExprClass.Variable;

			if (ec.TypeContainer.CurrentType != null)
				type = ec.TypeContainer.CurrentType;
			else
				type = ec.ContainerType;

			is_struct = ec.TypeContainer is Struct;

			if (ec.IsStatic) {
				Error (26, "Keyword `this' is not valid in a static property, " +
				       "static method, or static field initializer");
				return false;
			}

			if (block != null) {
				if (block.Toplevel.ThisVariable != null)
					variable_info = block.Toplevel.ThisVariable.VariableInfo;

				AnonymousContainer am = ec.CurrentAnonymousMethod;
				if (is_struct && (am != null) && !am.IsIterator) {
					Report.Error (1673, loc, "Anonymous methods inside structs " +
						      "cannot access instance members of `this'. " +
						      "Consider copying `this' to a local variable " +
						      "outside the anonymous method and using the " +
						      "local instead.");
				}

				RootScopeInfo host = block.Toplevel.RootScope;
				if ((host != null) && !ec.IsConstructor &&
				    (!is_struct || host.IsIterator)) {
					variable = host.CaptureThis ();
					type = variable.Type;
					is_struct = false;
				}
			}

			if (variable == null)
				variable = new SimpleThis (type);
			
			return true;
		}

		//
		// Called from Invocation to check if the invocation is correct
		//
		public override void CheckMarshalByRefAccess (EmitContext ec)
		{
			if ((variable_info != null) && !(type.IsValueType && ec.OmitStructFlowAnalysis) &&
			    !variable_info.IsAssigned (ec)) {
				Error (188, "The `this' object cannot be used before all of its " +
				       "fields are assigned to");
				variable_info.SetAssigned (ec);
			}
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (2);
			args.Add (new Argument (this));
			args.Add (new Argument (new TypeOf (new TypeExpression (type, loc), loc)));
			return CreateExpressionFactoryCall ("Constant", args);
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			if (!ResolveBase (ec))
				return null;


			if (ec.IsInFieldInitializer) {
				Error (27, "Keyword `this' is not available in the current context");
				return null;
			}
			
			return this;
		}

		override public Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			if (!ResolveBase (ec))
				return null;

			if (variable_info != null)
				variable_info.SetAssigned (ec);
			
			if (ec.TypeContainer is Class){
				Error (1604, "Cannot assign to 'this' because it is read-only");
				return null;
			}

			return this;
		}
		public override int GetHashCode()
		{
			return block.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			This t = obj as This;
			if (t == null)
				return false;

			return block == t.block;
		}

		protected class SimpleThis : Variable
		{
			Type type;

			public SimpleThis (Type type)
			{
				this.type = type;
			}

			public override Type Type {
				get { return type; }
			}

			public override bool HasInstance {
				get { return false; }
			}

			public override bool NeedsTemporary {
				get { return false; }
			}

			public override void EmitInstance (EmitContext ec)
			{
				// Do nothing.
			}

			public override void Emit (EmitContext ec)
			{
				ec.ig.Emit (OpCodes.Ldarg_0);
			}

			public override void EmitAssign (EmitContext ec)
			{
				throw new InvalidOperationException ();
			}

			public override void EmitAddressOf (EmitContext ec)
			{
				ec.ig.Emit (OpCodes.Ldarg_0);
			}
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			This target = (This) t;

			target.block = clonectx.LookupBlock (block);
		}
	}

	/// <summary>
	///   Represents the `__arglist' construct
	/// </summary>
	public class ArglistAccess : Expression
	{
		public ArglistAccess (Location loc)
		{
			this.loc = loc;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			eclass = ExprClass.Variable;
			type = TypeManager.runtime_argument_handle_type;

			if (ec.IsInFieldInitializer || !ec.CurrentBlock.Toplevel.HasVarargs) 
			{
				Error (190, "The __arglist construct is valid only within " +
				       "a variable argument method");
				return null;
			}

			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ec.ig.Emit (OpCodes.Arglist);
		}

		protected override void CloneTo (CloneContext clonectx, Expression target)
		{
			// nothing.
		}
	}

	/// <summary>
	///   Represents the `__arglist (....)' construct
	/// </summary>
	public class Arglist : Expression
	{
		Argument[] Arguments;

		public Arglist (Location loc)
			: this (Argument.Empty, loc)
		{
		}

		public Arglist (Argument[] args, Location l)
		{
			Arguments = args;
			loc = l;
		}

		public Type[] ArgumentTypes {
			get {
				Type[] retval = new Type [Arguments.Length];
				for (int i = 0; i < Arguments.Length; i++)
					retval [i] = Arguments [i].Type;
				return retval;
			}
		}
		
		public override Expression CreateExpressionTree (EmitContext ec)
		{
			Report.Error (1952, loc, "An expression tree cannot contain a method with variable arguments");
			return null;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			eclass = ExprClass.Variable;
			type = TypeManager.runtime_argument_handle_type;

			foreach (Argument arg in Arguments) {
				if (!arg.Resolve (ec, loc))
					return null;
			}

			return this;
		}

		public override void Emit (EmitContext ec)
		{
			foreach (Argument arg in Arguments)
				arg.Emit (ec);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			Arglist target = (Arglist) t;

			target.Arguments = new Argument [Arguments.Length];
			for (int i = 0; i < Arguments.Length; i++)
				target.Arguments [i] = Arguments [i].Clone (clonectx);
		}
	}

	//
	// This produces the value that renders an instance, used by the iterators code
	//
	public class ProxyInstance : Expression, IMemoryLocation  {
		public override Expression DoResolve (EmitContext ec)
		{
			eclass = ExprClass.Variable;
			type = ec.ContainerType;
			return this;
		}
		
		public override void Emit (EmitContext ec)
		{
			ec.ig.Emit (OpCodes.Ldarg_0);

		}
		
		public void AddressOf (EmitContext ec, AddressOp mode)
		{
			ec.ig.Emit (OpCodes.Ldarg_0);
		}
	}

	/// <summary>
	///   Implements the typeof operator
	/// </summary>
	public class TypeOf : Expression {
		Expression QueriedType;
		protected Type typearg;
		
		public TypeOf (Expression queried_type, Location l)
		{
			QueriedType = queried_type;
			loc = l;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			TypeExpr texpr = QueriedType.ResolveAsTypeTerminal (ec, false);
			if (texpr == null)
				return null;

			typearg = texpr.Type;

			if (typearg == TypeManager.void_type) {
				Error (673, "System.Void cannot be used from C#. Use typeof (void) to get the void type object");
				return null;
			}

			if (typearg.IsPointer && !ec.InUnsafe){
				UnsafeError (loc);
				return null;
			}

			type = TypeManager.type_type;
			// Even though what is returned is a type object, it's treated as a value by the compiler.
			// In particular, 'typeof (Foo).X' is something totally different from 'Foo.X'.
			eclass = ExprClass.Value;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ec.ig.Emit (OpCodes.Ldtoken, typearg);
			ec.ig.Emit (OpCodes.Call, TypeManager.system_type_get_type_from_handle);
		}

		public override bool GetAttributableValue (Type value_type, out object value)
		{
			if (TypeManager.ContainsGenericParameters (typearg) &&
				!TypeManager.IsGenericTypeDefinition (typearg)) {
				Report.SymbolRelatedToPreviousError (typearg);
				Report.Error (416, loc, "`{0}': an attribute argument cannot use type parameters",
					     TypeManager.CSharpName (typearg));
				value = null;
				return false;
			}

			if (value_type == TypeManager.object_type) {
				value = (object)typearg;
				return true;
			}
			value = typearg;
			return true;
		}

		public Type TypeArgument
		{
			get
			{
				return typearg;
			}
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			TypeOf target = (TypeOf) t;

			target.QueriedType = QueriedType.Clone (clonectx);
		}
	}

	/// <summary>
	///   Implements the `typeof (void)' operator
	/// </summary>
	public class TypeOfVoid : TypeOf {
		public TypeOfVoid (Location l) : base (null, l)
		{
			loc = l;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			type = TypeManager.type_type;
			typearg = TypeManager.void_type;
			// See description in TypeOf.
			eclass = ExprClass.Value;
			return this;
		}
	}

	internal class TypeOfMethod : Expression
	{
		readonly MethodInfo method;
		static MethodInfo get_type_from_handle;

		static TypeOfMethod ()
		{
			get_type_from_handle = typeof (MethodBase).GetMethod ("GetMethodFromHandle",
				new Type [] { TypeManager.runtime_method_handle_type });
		}

		public TypeOfMethod (MethodInfo method, Location loc)
		{
			this.method = method;
			this.loc = loc;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			type = typeof (MethodBase);
			eclass = ExprClass.Value;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ec.ig.Emit (OpCodes.Ldtoken, method);
			ec.ig.Emit (OpCodes.Call, get_type_from_handle);
		}
	}

	/// <summary>
	///   Implements the sizeof expression
	/// </summary>
	public class SizeOf : Expression {
		readonly Expression QueriedType;
		Type type_queried;
		
		public SizeOf (Expression queried_type, Location l)
		{
			this.QueriedType = queried_type;
			loc = l;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			TypeExpr texpr = QueriedType.ResolveAsTypeTerminal (ec, false);
			if (texpr == null)
				return null;

#if GMCS_SOURCE
			if (texpr is TypeParameterExpr){
				((TypeParameterExpr)texpr).Error_CannotUseAsUnmanagedType (loc);
				return null;
			}
#endif

			type_queried = texpr.Type;
			if (type_queried.IsEnum)
				type_queried = TypeManager.EnumToUnderlying (type_queried);

			if (type_queried == TypeManager.void_type) {
				Expression.Error_VoidInvalidInTheContext (loc);
				return null;
			}

			int size_of = GetTypeSize (type_queried);
			if (size_of > 0) {
				return new IntConstant (size_of, loc);
			}

			if (!ec.InUnsafe) {
				Report.Error (233, loc, "`{0}' does not have a predefined size, therefore sizeof can only be used in an unsafe context (consider using System.Runtime.InteropServices.Marshal.SizeOf)",
					 TypeManager.CSharpName (type_queried));
				return null;
			}

			if (!TypeManager.VerifyUnManaged (type_queried, loc)){
				return null;
			}
			
			type = TypeManager.int32_type;
			eclass = ExprClass.Value;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			int size = GetTypeSize (type_queried);

			if (size == 0)
				ec.ig.Emit (OpCodes.Sizeof, type_queried);
			else
				IntConstant.EmitInt (ec.ig, size);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
		}
	}

	/// <summary>
	///   Implements the qualified-alias-member (::) expression.
	/// </summary>
	public class QualifiedAliasMember : Expression
	{
		string alias, identifier;

		public QualifiedAliasMember (string alias, string identifier, Location l)
		{
			this.alias = alias;
			this.identifier = identifier;
			loc = l;
		}

		public override FullNamedExpression ResolveAsTypeStep (IResolveContext ec, bool silent)
		{
			if (alias == "global")
				return new MemberAccess (RootNamespace.Global, identifier, loc).ResolveAsTypeStep (ec, silent);

			int errors = Report.Errors;
			FullNamedExpression fne = ec.DeclContainer.NamespaceEntry.LookupAlias (alias);
			if (fne == null) {
				if (errors == Report.Errors)
					Report.Error (432, loc, "Alias `{0}' not found", alias);
				return null;
			}
			if (fne.eclass != ExprClass.Namespace) {
				if (!silent)
					Report.Error (431, loc, "`{0}' cannot be used with '::' since it denotes a type", alias);
				return null;
			}
			return new MemberAccess (fne, identifier).ResolveAsTypeStep (ec, silent);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			FullNamedExpression fne;
			if (alias == "global") {
				fne = RootNamespace.Global;
			} else {
				int errors = Report.Errors;
				fne = ec.DeclContainer.NamespaceEntry.LookupAlias (alias);
				if (fne == null) {
					if (errors == Report.Errors)
						Report.Error (432, loc, "Alias `{0}' not found", alias);
					return null;
				}
			}

			Expression retval = new MemberAccess (fne, identifier).DoResolve (ec);
			if (retval == null)
				return null;

			if (!(retval is FullNamedExpression)) {
				Report.Error (687, loc, "The expression `{0}::{1}' did not resolve to a namespace or a type", alias, identifier);
				return null;
			}

			// We defer this check till the end to match the behaviour of CSC
			if (fne.eclass != ExprClass.Namespace) {
				Report.Error (431, loc, "`{0}' cannot be used with '::' since it denotes a type", alias);
				return null;
			}
			return retval;
		}

		public override void Emit (EmitContext ec)
		{
			throw new InternalErrorException ("QualifiedAliasMember found in resolved tree");
		}


		public override string ToString ()
		{
			return alias + "::" + identifier;
		}

		public override string GetSignatureForError ()
		{
			return ToString ();
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			// Nothing 
		}
	}

	/// <summary>
	///   Implements the member access expression
	/// </summary>
	public class MemberAccess : Expression {
		public readonly string Identifier;
		Expression expr;
		readonly TypeArguments args;

		public MemberAccess (Expression expr, string id)
			: this (expr, id, expr.Location)
		{
		}

		public MemberAccess (Expression expr, string identifier, Location loc)
		{
			this.expr = expr;
			Identifier = identifier;
			this.loc = loc;
		}

		public MemberAccess (Expression expr, string identifier, TypeArguments args, Location loc)
			: this (expr, identifier, loc)
		{
			this.args = args;
		}

		protected string LookupIdentifier {
			get { return MemberName.MakeName (Identifier, args); }
		}

		// TODO: this method has very poor performace for Enum fields and
		// probably for other constants as well
		Expression DoResolve (EmitContext ec, Expression right_side)
		{
			if (type != null)
				throw new Exception ();

			//
			// Resolve the expression with flow analysis turned off, we'll do the definite
			// assignment checks later.  This is because we don't know yet what the expression
			// will resolve to - it may resolve to a FieldExpr and in this case we must do the
			// definite assignment check on the actual field and not on the whole struct.
			//

			SimpleName original = expr as SimpleName;
			Expression expr_resolved = expr.Resolve (ec,
				ResolveFlags.VariableOrValue | ResolveFlags.Type |
				ResolveFlags.Intermediate | ResolveFlags.DisableStructFlowAnalysis);

			if (expr_resolved == null)
				return null;

			if (expr_resolved is Namespace) {
				Namespace ns = (Namespace) expr_resolved;
				FullNamedExpression retval = ns.Lookup (ec.DeclContainer, LookupIdentifier, loc);
#if GMCS_SOURCE
				if ((retval != null) && (args != null))
					retval = new ConstructedType (retval, args, loc).ResolveAsTypeStep (ec, false);
#endif

				if (retval == null)
					ns.Error_NamespaceDoesNotExist (ec.DeclContainer, loc, Identifier);
				return retval;
			}

			Type expr_type = expr_resolved.Type;
			if (expr_type.IsPointer || expr_type == TypeManager.void_type || expr_resolved is NullLiteral){
				Unary.Error_OperatorCannotBeApplied (loc, ".", expr_type);
				return null;
			}
			if (expr_type == TypeManager.anonymous_method_type){
				Unary.Error_OperatorCannotBeApplied (loc, ".", "anonymous method");
				return null;
			}

			Constant c = expr_resolved as Constant;
			if (c != null && c.GetValue () == null) {
				Report.Warning (1720, 1, loc, "Expression will always cause a `{0}'",
					"System.NullReferenceException");
			}

			if (args != null) {
				if (!args.Resolve (ec))
					return null;
			}

			Expression member_lookup;
			member_lookup = MemberLookup (
				ec.ContainerType, expr_type, expr_type, Identifier, loc);
#if GMCS_SOURCE
			if ((member_lookup == null) && (args != null)) {
				member_lookup = MemberLookup (
					ec.ContainerType, expr_type, expr_type, LookupIdentifier, loc);
			}
#endif
			if (member_lookup == null) {
				ExprClass expr_eclass = expr_resolved.eclass;

				//
				// Extension methods are not allowed on all expression types
				//
				if (expr_eclass == ExprClass.Value || expr_eclass == ExprClass.Variable ||
					expr_eclass == ExprClass.IndexerAccess || expr_eclass == ExprClass.PropertyAccess ||
					expr_eclass == ExprClass.EventAccess) {
					ExtensionMethodGroupExpr ex_method_lookup = ec.TypeContainer.LookupExtensionMethod (expr_type, Identifier);
					if (ex_method_lookup != null) {
						ex_method_lookup.ExtensionExpression = expr_resolved;

						if (args != null) {
							ex_method_lookup.SetTypeArguments (args);
						}

						return ex_method_lookup.DoResolve (ec);
					}
				}

				expr = expr_resolved;
				Error_MemberLookupFailed (
					ec.ContainerType, expr_type, expr_type, Identifier, null,
					AllMemberTypes, AllBindingFlags);
				return null;
			}

			TypeExpr texpr = member_lookup as TypeExpr;
			if (texpr != null) {
				if (!(expr_resolved is TypeExpr) && 
				    (original == null || !original.IdenticalNameAndTypeName (ec, expr_resolved, loc))) {
					Report.Error (572, loc, "`{0}': cannot reference a type through an expression; try `{1}' instead",
						Identifier, member_lookup.GetSignatureForError ());
					return null;
				}

				if (!texpr.CheckAccessLevel (ec.DeclContainer)) {
					Report.SymbolRelatedToPreviousError (member_lookup.Type);
					ErrorIsInaccesible (loc, TypeManager.CSharpName (member_lookup.Type));
					return null;
				}

#if GMCS_SOURCE
				ConstructedType ct = expr_resolved as ConstructedType;
				if (ct != null) {
					//
					// When looking up a nested type in a generic instance
					// via reflection, we always get a generic type definition
					// and not a generic instance - so we have to do this here.
					//
					// See gtest-172-lib.cs and gtest-172.cs for an example.
					//
					ct = new ConstructedType (
						member_lookup.Type, ct.TypeArguments, loc);

					return ct.ResolveAsTypeStep (ec, false);
				}
#endif
				return member_lookup;
			}

			MemberExpr me = (MemberExpr) member_lookup;
			me = me.ResolveMemberAccess (ec, expr_resolved, loc, original);
			if (me == null)
				return null;

			if (args != null) {
				me.SetTypeArguments (args);
			}

			if (original != null && !TypeManager.IsValueType (expr_type)) {
				if (me.IsInstance) {
					LocalVariableReference var = expr_resolved as LocalVariableReference;
					if (var != null && !var.VerifyAssigned (ec))
						return null;
				}
			}

			// The following DoResolve/DoResolveLValue will do the definite assignment
			// check.

			if (right_side != null)
				return me.DoResolveLValue (ec, right_side);
			else
				return me.DoResolve (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return DoResolve (ec, null);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			return DoResolve (ec, right_side);
		}

		public override FullNamedExpression ResolveAsTypeStep (IResolveContext ec, bool silent)
		{
			return ResolveNamespaceOrType (ec, silent);
		}

		public FullNamedExpression ResolveNamespaceOrType (IResolveContext rc, bool silent)
		{
			FullNamedExpression new_expr = expr.ResolveAsTypeStep (rc, silent);

			if (new_expr == null)
				return null;

			if (new_expr is Namespace) {
				Namespace ns = (Namespace) new_expr;
				FullNamedExpression retval = ns.Lookup (rc.DeclContainer, LookupIdentifier, loc);
#if GMCS_SOURCE
				if ((retval != null) && (args != null))
					retval = new ConstructedType (retval, args, loc).ResolveAsTypeStep (rc, false);
#endif
				if (!silent && retval == null)
					ns.Error_NamespaceDoesNotExist (rc.DeclContainer, loc, LookupIdentifier);
				return retval;
			}

			TypeExpr tnew_expr = new_expr.ResolveAsTypeTerminal (rc, false);
			if (tnew_expr == null)
				return null;

			Type expr_type = tnew_expr.Type;

			if (expr_type.IsPointer){
				Error (23, "The `.' operator can not be applied to pointer operands (" +
				       TypeManager.CSharpName (expr_type) + ")");
				return null;
			}

			Expression member_lookup = MemberLookup (
				rc.DeclContainer.TypeBuilder, expr_type, expr_type, LookupIdentifier,
				MemberTypes.NestedType, BindingFlags.Public | BindingFlags.NonPublic, loc);
			if (member_lookup == null) {
				if (silent)
					return null;

				member_lookup = MemberLookup (
					rc.DeclContainer.TypeBuilder, expr_type, expr_type, SimpleName.RemoveGenericArity (LookupIdentifier),
					MemberTypes.NestedType, BindingFlags.Public | BindingFlags.NonPublic, loc);

				if (member_lookup != null) {
					tnew_expr = member_lookup.ResolveAsTypeTerminal (rc, false);
					if (tnew_expr == null)
						return null;

					Namespace.Error_TypeArgumentsCannotBeUsed (tnew_expr.Type, loc);
					return null;
				}

				member_lookup = MemberLookup (
					rc.DeclContainer.TypeBuilder, expr_type, expr_type, LookupIdentifier,
						MemberTypes.All, BindingFlags.Public | BindingFlags.NonPublic, loc);

				if (member_lookup == null) {
					Report.Error (426, loc, "The nested type `{0}' does not exist in the type `{1}'",
							  Identifier, new_expr.GetSignatureForError ());
				} else {
					// TODO: Report.SymbolRelatedToPreviousError
					member_lookup.Error_UnexpectedKind (null, "type", loc);
				}
				return null;
			}

			TypeExpr texpr = member_lookup.ResolveAsTypeTerminal (rc, false);
			if (texpr == null)
				return null;

#if GMCS_SOURCE
			TypeArguments the_args = args;
			Type declaring_type = texpr.Type.DeclaringType;
			if (TypeManager.HasGenericArguments (declaring_type)) {
				while (!TypeManager.IsEqual (TypeManager.DropGenericTypeArguments (expr_type), declaring_type)) {
					expr_type = expr_type.BaseType;
				}
				
				TypeArguments new_args = new TypeArguments (loc);
				foreach (Type decl in TypeManager.GetTypeArguments (expr_type))
					new_args.Add (new TypeExpression (decl, loc));

				if (args != null)
					new_args.Add (args);

				the_args = new_args;
			}

			if (the_args != null) {
				ConstructedType ctype = new ConstructedType (texpr.Type, the_args, loc);
				return ctype.ResolveAsTypeStep (rc, false);
			}
#endif

			return texpr;
		}

		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Should not happen");
		}

		protected override void Error_TypeDoesNotContainDefinition (Type type, string name)
		{
			if (RootContext.Version > LanguageVersion.ISO_2 &&
				((expr.eclass & (ExprClass.Value | ExprClass.Variable)) != 0)) {
				Report.Error (1061, loc, "Type `{0}' does not contain a definition for `{1}' and no " +
					"extension method `{1}' of type `{0}' could be found " +
					"(are you missing a using directive or an assembly reference?)",
					TypeManager.CSharpName (type), name);
				return;
			}

			base.Error_TypeDoesNotContainDefinition (type, name);
		}

		public override string ToString ()
		{
			return expr + "." + MemberName.MakeName (Identifier, args);
		}

		public override string GetSignatureForError ()
		{
			return expr.GetSignatureForError () + "." + Identifier;
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			MemberAccess target = (MemberAccess) t;

			target.expr = expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   Implements checked expressions
	/// </summary>
	public class CheckedExpr : Expression {

		public Expression Expr;

		public CheckedExpr (Expression e, Location l)
		{
			Expr = e;
			loc = l;
		}
		
		public override Expression CreateExpressionTree (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, true))
				return Expr.CreateExpressionTree (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, true))
				Expr = Expr.Resolve (ec);
			
			if (Expr == null)
				return null;

			if (Expr is Constant)
				return Expr;
			
			eclass = Expr.eclass;
			type = Expr.Type;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, true))
				Expr.Emit (ec);
		}

		public override void EmitBranchable (EmitContext ec, Label target, bool on_true)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, true))
				Expr.EmitBranchable (ec, target, on_true);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			CheckedExpr target = (CheckedExpr) t;

			target.Expr = Expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   Implements the unchecked expression
	/// </summary>
	public class UnCheckedExpr : Expression {

		public Expression Expr;

		public UnCheckedExpr (Expression e, Location l)
		{
			Expr = e;
			loc = l;
		}
		
		public override Expression CreateExpressionTree (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, false))
				return Expr.CreateExpressionTree (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, false))
				Expr = Expr.Resolve (ec);

			if (Expr == null)
				return null;

			if (Expr is Constant)
				return Expr;
			
			eclass = Expr.eclass;
			type = Expr.Type;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, false))
				Expr.Emit (ec);
		}
		
		public override void EmitBranchable (EmitContext ec, Label target, bool on_true)
		{
			using (ec.With (EmitContext.Flags.AllCheckStateFlags, false))
				Expr.EmitBranchable (ec, target, on_true);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			UnCheckedExpr target = (UnCheckedExpr) t;

			target.Expr = Expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   An Element Access expression.
	///
	///   During semantic analysis these are transformed into 
	///   IndexerAccess, ArrayAccess or a PointerArithmetic.
	/// </summary>
	public class ElementAccess : Expression {
		public ArrayList  Arguments;
		public Expression Expr;
		
		public ElementAccess (Expression e, ArrayList e_list)
		{
			Expr = e;

			loc  = e.Location;
			
			if (e_list == null)
				return;
			
			Arguments = new ArrayList ();
			foreach (Expression tmp in e_list)
				Arguments.Add (new Argument (tmp, Argument.AType.Expression));
			
		}

		bool CommonResolve (EmitContext ec)
		{
			Expr = Expr.Resolve (ec);

			if (Arguments == null)
				return false;

			foreach (Argument a in Arguments){
				if (!a.Resolve (ec, loc))
					return false;
			}

			return Expr != null;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (Arguments.Count + 1);
			args.Add (new Argument (Expr.CreateExpressionTree (ec)));
			foreach (Argument a in Arguments)
				args.Add (new Argument (a.Expr.CreateExpressionTree (ec)));

			return CreateExpressionFactoryCall ("ArrayIndex", args);
		}

		Expression MakePointerAccess (EmitContext ec, Type t)
		{
			if (t == TypeManager.void_ptr_type){
				Error (242, "The array index operation is not valid on void pointers");
				return null;
			}
			if (Arguments.Count != 1){
				Error (196, "A pointer must be indexed by only one value");
				return null;
			}
			Expression p;

			p = new PointerArithmetic (true, Expr, ((Argument)Arguments [0]).Expr, t, loc).Resolve (ec);
			if (p == null)
				return null;
			return new Indirection (p, loc).Resolve (ec);
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			if (!CommonResolve (ec))
				return null;

			//
			// We perform some simple tests, and then to "split" the emit and store
			// code we create an instance of a different class, and return that.
			//
			// I am experimenting with this pattern.
			//
			Type t = Expr.Type;

			if (t == TypeManager.array_type){
				Report.Error (21, loc, "Cannot apply indexing with [] to an expression of type `System.Array'");
				return null;
			}
			
			if (t.IsArray)
				return (new ArrayAccess (this, loc)).Resolve (ec);
			if (t.IsPointer)
				return MakePointerAccess (ec, t);

			FieldExpr fe = Expr as FieldExpr;
			if (fe != null) {
				IFixedBuffer ff = AttributeTester.GetFixedBuffer (fe.FieldInfo);
				if (ff != null) {
					return MakePointerAccess (ec, ff.ElementType);
				}
			}
			return (new IndexerAccess (this, loc)).Resolve (ec);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			if (!CommonResolve (ec))
				return null;

			type = Expr.Type;
			if (type.IsArray)
				return (new ArrayAccess (this, loc)).DoResolveLValue (ec, right_side);

			if (type.IsPointer)
				return MakePointerAccess (ec, type);

			if (Expr.eclass != ExprClass.Variable && type.IsValueType)
				Error_CannotModifyIntermediateExpressionValue (ec);

			return (new IndexerAccess (this, loc)).DoResolveLValue (ec, right_side);
		}
		
		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Should never be reached");
		}

		public override string GetSignatureForError ()
		{
			return Expr.GetSignatureForError ();
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			ElementAccess target = (ElementAccess) t;

			target.Expr = Expr.Clone (clonectx);
			target.Arguments = new ArrayList (Arguments.Count);
			foreach (Argument a in Arguments)
				target.Arguments.Add (a.Clone (clonectx));
		}
	}

	/// <summary>
	///   Implements array access 
	/// </summary>
	public class ArrayAccess : Expression, IAssignMethod, IMemoryLocation {
		//
		// Points to our "data" repository
		//
		ElementAccess ea;

		LocalTemporary temp;
		LocalTemporary prepared_value;

		bool prepared;
		
		public ArrayAccess (ElementAccess ea_data, Location l)
		{
			ea = ea_data;
			eclass = ExprClass.Variable;
			loc = l;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			return ea.CreateExpressionTree (ec);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			return DoResolve (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
#if false
			ExprClass eclass = ea.Expr.eclass;

			// As long as the type is valid
			if (!(eclass == ExprClass.Variable || eclass == ExprClass.PropertyAccess ||
			      eclass == ExprClass.Value)) {
				ea.Expr.Error_UnexpectedKind ("variable or value");
				return null;
			}
#endif

			Type t = ea.Expr.Type;
			if (t.GetArrayRank () != ea.Arguments.Count) {
				Report.Error (22, ea.Location, "Wrong number of indexes `{0}' inside [], expected `{1}'",
					  ea.Arguments.Count.ToString (), t.GetArrayRank ().ToString ());
				return null;
			}

			type = TypeManager.GetElementType (t);
			if (type.IsPointer && !ec.InUnsafe) {
				UnsafeError (ea.Location);
				return null;
			}

			foreach (Argument a in ea.Arguments) {
				a.Expr = ConvertExpressionToArrayIndex (ec, a.Expr);
			}
			
			eclass = ExprClass.Variable;

			return this;
		}

		/// <summary>
		///    Emits the right opcode to load an object of Type `t'
		///    from an array of T
		/// </summary>
		void EmitLoadOpcode (ILGenerator ig, Type type, int rank)
		{
			if (rank > 1) {
				MethodInfo get = FetchGetMethod ();
				ig.Emit (OpCodes.Call, get);
				return;
			}

			if (type == TypeManager.byte_type || type == TypeManager.bool_type)
				ig.Emit (OpCodes.Ldelem_U1);
			else if (type == TypeManager.sbyte_type)
				ig.Emit (OpCodes.Ldelem_I1);
			else if (type == TypeManager.short_type)
				ig.Emit (OpCodes.Ldelem_I2);
			else if (type == TypeManager.ushort_type || type == TypeManager.char_type)
				ig.Emit (OpCodes.Ldelem_U2);
			else if (type == TypeManager.int32_type)
				ig.Emit (OpCodes.Ldelem_I4);
			else if (type == TypeManager.uint32_type)
				ig.Emit (OpCodes.Ldelem_U4);
			else if (type == TypeManager.uint64_type)
				ig.Emit (OpCodes.Ldelem_I8);
			else if (type == TypeManager.int64_type)
				ig.Emit (OpCodes.Ldelem_I8);
			else if (type == TypeManager.float_type)
				ig.Emit (OpCodes.Ldelem_R4);
			else if (type == TypeManager.double_type)
				ig.Emit (OpCodes.Ldelem_R8);
			else if (type == TypeManager.intptr_type)
				ig.Emit (OpCodes.Ldelem_I);
			else if (TypeManager.IsEnumType (type)){
				EmitLoadOpcode (ig, TypeManager.EnumToUnderlying (type), rank);
			} else if (type.IsValueType){
				ig.Emit (OpCodes.Ldelema, type);
				ig.Emit (OpCodes.Ldobj, type);
#if GMCS_SOURCE
			} else if (type.IsGenericParameter) {
				ig.Emit (OpCodes.Ldelem, type);
#endif
			} else if (type.IsPointer)
				ig.Emit (OpCodes.Ldelem_I);
			else
				ig.Emit (OpCodes.Ldelem_Ref);
		}

		protected override void Error_NegativeArrayIndex (Location loc)
		{
			Report.Warning (251, 2, loc, "Indexing an array with a negative index (array indices always start at zero)");
		}

		/// <summary>
		///    Returns the right opcode to store an object of Type `t'
		///    from an array of T.  
		/// </summary>
		static public OpCode GetStoreOpcode (Type t, out bool is_stobj, out bool has_type_arg)
		{
			//Console.WriteLine (new System.Diagnostics.StackTrace ());
			has_type_arg = false; is_stobj = false;
			t = TypeManager.TypeToCoreType (t);
			if (TypeManager.IsEnumType (t))
				t = TypeManager.EnumToUnderlying (t);
			if (t == TypeManager.byte_type || t == TypeManager.sbyte_type ||
			    t == TypeManager.bool_type)
				return OpCodes.Stelem_I1;
			else if (t == TypeManager.short_type || t == TypeManager.ushort_type ||
				 t == TypeManager.char_type)
				return OpCodes.Stelem_I2;
			else if (t == TypeManager.int32_type || t == TypeManager.uint32_type)
				return OpCodes.Stelem_I4;
			else if (t == TypeManager.int64_type || t == TypeManager.uint64_type)
				return OpCodes.Stelem_I8;
			else if (t == TypeManager.float_type)
				return OpCodes.Stelem_R4;
			else if (t == TypeManager.double_type)
				return OpCodes.Stelem_R8;
			else if (t == TypeManager.intptr_type) {
                                has_type_arg = true;
				is_stobj = true;
                                return OpCodes.Stobj;
			} else if (t.IsValueType) {
				has_type_arg = true;
				is_stobj = true;
				return OpCodes.Stobj;
#if GMCS_SOURCE
			} else if (t.IsGenericParameter) {
				has_type_arg = true;
				return OpCodes.Stelem;
#endif

			} else if (t.IsPointer)
				return OpCodes.Stelem_I;
			else
				return OpCodes.Stelem_Ref;
		}

		MethodInfo FetchGetMethod ()
		{
			ModuleBuilder mb = CodeGen.Module.Builder;
			int arg_count = ea.Arguments.Count;
			Type [] args = new Type [arg_count];
			MethodInfo get;
			
			for (int i = 0; i < arg_count; i++){
				//args [i++] = a.Type;
				args [i] = TypeManager.int32_type;
			}
			
			get = mb.GetArrayMethod (
				ea.Expr.Type, "Get",
				CallingConventions.HasThis |
				CallingConventions.Standard,
				type, args);
			return get;
		}
				

		MethodInfo FetchAddressMethod ()
		{
			ModuleBuilder mb = CodeGen.Module.Builder;
			int arg_count = ea.Arguments.Count;
			Type [] args = new Type [arg_count];
			MethodInfo address;
			Type ret_type;
			
			ret_type = TypeManager.GetReferenceType (type);
			
			for (int i = 0; i < arg_count; i++){
				//args [i++] = a.Type;
				args [i] = TypeManager.int32_type;
			}
			
			address = mb.GetArrayMethod (
				ea.Expr.Type, "Address",
				CallingConventions.HasThis |
				CallingConventions.Standard,
				ret_type, args);

			return address;
		}

		//
		// Load the array arguments into the stack.
		//
		// If we have been requested to cache the values (cached_locations array
		// initialized), then load the arguments the first time and store them
		// in locals.  otherwise load from local variables.
		//
		// prepare_for_load is used in compound assignments to cache original index
		// values ( label[idx++] += s )
		//
		LocalTemporary [] LoadArrayAndArguments (EmitContext ec, bool prepare_for_load)
		{
			ea.Expr.Emit (ec);

			LocalTemporary[] indexes = null;
			if (prepare_for_load) {
				ec.ig.Emit (OpCodes.Dup);
				indexes = new LocalTemporary [ea.Arguments.Count];
			}

			for (int i = 0; i < ea.Arguments.Count; ++i) {
				((Argument)ea.Arguments [i]).Emit (ec);
				if (!prepare_for_load)
					continue;

				// Keep original array index value on the stack
				ec.ig.Emit (OpCodes.Dup);

				indexes [i] = new LocalTemporary (TypeManager.intptr_type);
				indexes [i].Store (ec);
			}

			return indexes;
		}

		public void Emit (EmitContext ec, bool leave_copy)
		{
			int rank = ea.Expr.Type.GetArrayRank ();
			ILGenerator ig = ec.ig;

			if (prepared_value != null) {
				prepared_value.Emit (ec);
			} else if (prepared) {
				LoadFromPtr (ig, this.type);
			} else {
				LoadArrayAndArguments (ec, false);
				EmitLoadOpcode (ig, type, rank);
			}	

			if (leave_copy) {
				ig.Emit (OpCodes.Dup);
				temp = new LocalTemporary (this.type);
				temp.Store (ec);
			}
		}
		
		public override void Emit (EmitContext ec)
		{
			Emit (ec, false);
		}

		public void EmitAssign (EmitContext ec, Expression source, bool leave_copy, bool prepare_for_load)
		{
			int rank = ea.Expr.Type.GetArrayRank ();
			ILGenerator ig = ec.ig;
			Type t = source.Type;
			prepared = prepare_for_load && !(source is StringConcat);

			if (prepared) {
				AddressOf (ec, AddressOp.LoadStore);
				ec.ig.Emit (OpCodes.Dup);
			} else {
				LocalTemporary[] original_indexes_values = LoadArrayAndArguments (ec,
					prepare_for_load && (source is StringConcat));

				if (original_indexes_values != null) {
					prepared_value = new LocalTemporary (type);
					EmitLoadOpcode (ig, type, rank);
					prepared_value.Store (ec);
					foreach (LocalTemporary lt in original_indexes_values) {
						lt.Emit (ec);
						lt.Release (ec);
					}
				}
			}

			if (rank == 1) {
				bool is_stobj, has_type_arg;
				OpCode op = GetStoreOpcode (t, out is_stobj, out has_type_arg);

				if (!prepared) {
					//
					// The stobj opcode used by value types will need
					// an address on the stack, not really an array/array
					// pair
					//
					if (is_stobj)
						ig.Emit (OpCodes.Ldelema, t);
				}
				
				source.Emit (ec);
				if (leave_copy) {
					ec.ig.Emit (OpCodes.Dup);
					temp = new LocalTemporary (this.type);
					temp.Store (ec);
				}
				
				if (prepared)
					StoreFromPtr (ig, t);
				else if (is_stobj)
					ig.Emit (OpCodes.Stobj, t);
				else if (has_type_arg)
					ig.Emit (op, t);
				else
					ig.Emit (op);
			} else {
				source.Emit (ec);
				if (leave_copy) {
					ec.ig.Emit (OpCodes.Dup);
					temp = new LocalTemporary (this.type);
					temp.Store (ec);
				}

				if (prepared) {
					StoreFromPtr (ig, t);
				} else {
					int arg_count = ea.Arguments.Count;
					Type [] args = new Type [arg_count + 1];
					for (int i = 0; i < arg_count; i++) {
						//args [i++] = a.Type;
						args [i] = TypeManager.int32_type;
					}
					args [arg_count] = type;

					MethodInfo set = CodeGen.Module.Builder.GetArrayMethod (
						ea.Expr.Type, "Set",
						CallingConventions.HasThis |
						CallingConventions.Standard,
						TypeManager.void_type, args);

					ig.Emit (OpCodes.Call, set);
				}
			}
			
			if (temp != null) {
				temp.Emit (ec);
				temp.Release (ec);
			}
		}

		public void AddressOf (EmitContext ec, AddressOp mode)
		{
			int rank = ea.Expr.Type.GetArrayRank ();
			ILGenerator ig = ec.ig;

			LoadArrayAndArguments (ec, false);

			if (rank == 1){
				ig.Emit (OpCodes.Ldelema, type);
			} else {
				MethodInfo address = FetchAddressMethod ();
				ig.Emit (OpCodes.Call, address);
			}
		}

		public void EmitGetLength (EmitContext ec, int dim)
		{
			int rank = ea.Expr.Type.GetArrayRank ();
			ILGenerator ig = ec.ig;

			ea.Expr.Emit (ec);
			if (rank == 1) {
				ig.Emit (OpCodes.Ldlen);
				ig.Emit (OpCodes.Conv_I4);
			} else {
				IntLiteral.EmitInt (ig, dim);
				ig.Emit (OpCodes.Callvirt, TypeManager.int_getlength_int);
			}
		}
	}

	/// <summary>
	///   Expressions that represent an indexer call.
	/// </summary>
	public class IndexerAccess : Expression, IAssignMethod
	{
		class IndexerMethodGroupExpr : MethodGroupExpr
		{
			public IndexerMethodGroupExpr (Indexers indexers, Location loc)
				: base (null, loc)
			{
				Methods = (MethodBase []) indexers.Methods.ToArray (typeof (MethodBase));
			}

			public override string Name {
				get {
					return "this";
				}
			}

			protected override int GetApplicableParametersCount (MethodBase method, ParameterData parameters)
			{
				//
				// Here is the trick, decrease number of arguments by 1 when only
				// available property method is setter. This makes overload resolution
				// work correctly for indexers.
				//
				
				if (method.Name [0] == 'g')
					return parameters.Count;

				return parameters.Count - 1;
			}
		}

		class Indexers
		{
			// Contains either property getter or setter
			public ArrayList Methods;
			public ArrayList Properties;

			Indexers ()
			{
			}

			void Append (Type caller_type, MemberInfo [] mi)
			{
				if (mi == null)
					return;

				foreach (PropertyInfo property in mi) {
					MethodInfo accessor = property.GetGetMethod (true);
					if (accessor == null)
						accessor = property.GetSetMethod (true);

					if (Methods == null) {
						Methods = new ArrayList ();
						Properties = new ArrayList ();
					}

					Methods.Add (accessor);
					Properties.Add (property);
				}
			}

			static MemberInfo [] GetIndexersForTypeOrInterface (Type caller_type, Type lookup_type)
			{
				string p_name = TypeManager.IndexerPropertyName (lookup_type);

				return TypeManager.MemberLookup (
					caller_type, caller_type, lookup_type, MemberTypes.Property,
					BindingFlags.Public | BindingFlags.Instance |
					BindingFlags.DeclaredOnly, p_name, null);
			}
			
			public static Indexers GetIndexersForType (Type caller_type, Type lookup_type) 
			{
				Indexers ix = new Indexers ();

	#if GMCS_SOURCE
				if (lookup_type.IsGenericParameter) {
					GenericConstraints gc = TypeManager.GetTypeParameterConstraints (lookup_type);
					if (gc == null)
						return ix;

					if (gc.HasClassConstraint)
						ix.Append (caller_type, GetIndexersForTypeOrInterface (caller_type, gc.ClassConstraint));

					Type[] ifaces = gc.InterfaceConstraints;
					foreach (Type itype in ifaces)
						ix.Append (caller_type, GetIndexersForTypeOrInterface (caller_type, itype));

					return ix;
				}
	#endif

				Type copy = lookup_type;
				while (copy != TypeManager.object_type && copy != null){
					ix.Append (caller_type, GetIndexersForTypeOrInterface (caller_type, copy));
					copy = copy.BaseType;
				}

				if (lookup_type.IsInterface) {
					Type [] ifaces = TypeManager.GetInterfaces (lookup_type);
					if (ifaces != null) {
						foreach (Type itype in ifaces)
							ix.Append (caller_type, GetIndexersForTypeOrInterface (caller_type, itype));
					}
				}

				return ix;
			}
		}

		enum AccessorType
		{
			Get,
			Set
		}

		//
		// Points to our "data" repository
		//
		MethodInfo get, set;
		bool is_base_indexer;
		bool prepared;
		LocalTemporary temp;
		LocalTemporary prepared_value;
		Expression set_expr;

		protected Type indexer_type;
		protected Type current_type;
		protected Expression instance_expr;
		protected ArrayList arguments;
		
		public IndexerAccess (ElementAccess ea, Location loc)
			: this (ea.Expr, false, loc)
		{
			this.arguments = ea.Arguments;
		}

		protected IndexerAccess (Expression instance_expr, bool is_base_indexer,
					 Location loc)
		{
			this.instance_expr = instance_expr;
			this.is_base_indexer = is_base_indexer;
			this.eclass = ExprClass.Value;
			this.loc = loc;
		}

		static string GetAccessorName (AccessorType at)
		{
			if (at == AccessorType.Set)
				return "set";

			if (at == AccessorType.Get)
				return "get";

			throw new NotImplementedException (at.ToString ());
		}

		protected virtual bool CommonResolve (EmitContext ec)
		{
			indexer_type = instance_expr.Type;
			current_type = ec.ContainerType;

			return true;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return ResolveAccessor (ec, AccessorType.Get);
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			if (right_side == EmptyExpression.OutAccess) {
				Report.Error (206, loc, "A property or indexer `{0}' may not be passed as an out or ref parameter",
					      GetSignatureForError ());
				return null;
			}

			// if the indexer returns a value type, and we try to set a field in it
			if (right_side == EmptyExpression.LValueMemberAccess || right_side == EmptyExpression.LValueMemberOutAccess) {
				Error_CannotModifyIntermediateExpressionValue (ec);
			}

			Expression e = ResolveAccessor (ec, AccessorType.Set);
			if (e == null)
				return null;

			set_expr = Convert.ImplicitConversion (ec, right_side, type, loc);
			return e;
		}

		Expression ResolveAccessor (EmitContext ec, AccessorType accessorType)
		{
			if (!CommonResolve (ec))
				return null;

			Indexers ilist = Indexers.GetIndexersForType (current_type, indexer_type);
			if (ilist.Methods == null) {
				Report.Error (21, loc, "Cannot apply indexing with [] to an expression of type `{0}'",
						  TypeManager.CSharpName (indexer_type));
				return null;
			}

			MethodGroupExpr mg = new IndexerMethodGroupExpr (ilist, loc);
			mg = mg.OverloadResolve (ec, ref arguments, false, loc);
			if (mg == null)
				return null;

			MethodInfo mi = (MethodInfo) mg;
			PropertyInfo pi = null;
			for (int i = 0; i < ilist.Methods.Count; ++i) {
				if (ilist.Methods [i] == mi) {
					pi = (PropertyInfo) ilist.Properties [i];
					break;
				}
			}

			type = pi.PropertyType;
			if (type.IsPointer && !ec.InUnsafe)
				UnsafeError (loc);

			MethodInfo accessor;
			if (accessorType == AccessorType.Get) {
				accessor = get = pi.GetGetMethod (true);
			} else {
				accessor = set = pi.GetSetMethod (true);
				if (accessor == null && pi.GetGetMethod (true) != null) {
					Report.SymbolRelatedToPreviousError (pi);
					Report.Error (200, loc, "The read only property or indexer `{0}' cannot be assigned to",
						TypeManager.GetFullNameSignature (pi));
					return null;
				}
			}

			if (accessor == null) {
				Report.SymbolRelatedToPreviousError (pi);
				Report.Error (154, loc, "The property or indexer `{0}' cannot be used in this context because it lacks a `{1}' accessor",
					TypeManager.GetFullNameSignature (pi), GetAccessorName (accessorType));
				return null;
			}

			//
			// Only base will allow this invocation to happen.
			//
			if (accessor.IsAbstract && this is BaseIndexerAccess) {
				Error_CannotCallAbstractBase (TypeManager.GetFullNameSignature (pi));
			}

			bool must_do_cs1540_check;
			if (!IsAccessorAccessible (ec.ContainerType, accessor, out must_do_cs1540_check)) {
				if (set == null)
					set = pi.GetSetMethod (true);
				else
					get = pi.GetGetMethod (true);

				if (set != null && get != null &&
					(set.Attributes & MethodAttributes.MemberAccessMask) != (get.Attributes & MethodAttributes.MemberAccessMask)) {
					Report.SymbolRelatedToPreviousError (accessor);
					Report.Error (271, loc, "The property or indexer `{0}' cannot be used in this context because a `{1}' accessor is inaccessible",
						TypeManager.GetFullNameSignature (pi), GetAccessorName (accessorType));
				} else {
					Report.SymbolRelatedToPreviousError (pi);
					ErrorIsInaccesible (loc, TypeManager.GetFullNameSignature (pi));
				}
			}

			instance_expr.CheckMarshalByRefAccess (ec);
			eclass = ExprClass.IndexerAccess;
			return this;
		}
		
		public void Emit (EmitContext ec, bool leave_copy)
		{
			if (prepared) {
				prepared_value.Emit (ec);
			} else {
				Invocation.EmitCall (ec, is_base_indexer, instance_expr, get,
					arguments, loc, false, false);
			}

			if (leave_copy) {
				ec.ig.Emit (OpCodes.Dup);
				temp = new LocalTemporary (Type);
				temp.Store (ec);
			}
		}
		
		//
		// source is ignored, because we already have a copy of it from the
		// LValue resolution and we have already constructed a pre-cached
		// version of the arguments (ea.set_arguments);
		//
		public void EmitAssign (EmitContext ec, Expression source, bool leave_copy, bool prepare_for_load)
		{
			prepared = prepare_for_load;
			Expression value = set_expr;

			if (prepared) {
				Invocation.EmitCall (ec, is_base_indexer, instance_expr, get,
					arguments, loc, true, false);

				prepared_value = new LocalTemporary (type);
				prepared_value.Store (ec);
				source.Emit (ec);
				prepared_value.Release (ec);

				if (leave_copy) {
					ec.ig.Emit (OpCodes.Dup);
					temp = new LocalTemporary (Type);
					temp.Store (ec);
				}
			} else if (leave_copy) {
				temp = new LocalTemporary (Type);
				source.Emit (ec);
				temp.Store (ec);
				value = temp;
			}
			
			arguments.Add (new Argument (value, Argument.AType.Expression));
			Invocation.EmitCall (ec, is_base_indexer, instance_expr, set, arguments, loc, false, prepared);
			
			if (temp != null) {
				temp.Emit (ec);
				temp.Release (ec);
			}
		}
		
		public override void Emit (EmitContext ec)
		{
			Emit (ec, false);
		}

		public override string GetSignatureForError ()
		{
			return TypeManager.CSharpSignature (get != null ? get : set, false);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			IndexerAccess target = (IndexerAccess) t;

			if (arguments != null){
				target.arguments = new ArrayList ();
				foreach (Argument a in arguments)
					target.arguments.Add (a.Clone (clonectx));
			}
			if (instance_expr != null)
				target.instance_expr = instance_expr.Clone (clonectx);
		}
	}

	/// <summary>
	///   The base operator for method names
	/// </summary>
	public class BaseAccess : Expression {
		public readonly string Identifier;
		TypeArguments args;

		public BaseAccess (string member, Location l)
		{
			this.Identifier = member;
			loc = l;
		}

		public BaseAccess (string member, TypeArguments args, Location l)
			: this (member, l)
		{
			this.args = args;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			Expression c = CommonResolve (ec);

			if (c == null)
				return null;

			//
			// MethodGroups use this opportunity to flag an error on lacking ()
			//
			if (!(c is MethodGroupExpr))
				return c.Resolve (ec);
			return c;
		}

		public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
		{
			Expression c = CommonResolve (ec);

			if (c == null)
				return null;

			//
			// MethodGroups use this opportunity to flag an error on lacking ()
			//
			if (! (c is MethodGroupExpr))
				return c.DoResolveLValue (ec, right_side);

			return c;
		}

		Expression CommonResolve (EmitContext ec)
		{
			Expression member_lookup;
			Type current_type = ec.ContainerType;
			Type base_type = current_type.BaseType;

			if (ec.IsStatic){
				Error (1511, "Keyword `base' is not available in a static method");
				return null;
			}

			if (ec.IsInFieldInitializer){
				Error (1512, "Keyword `base' is not available in the current context");
				return null;
			}
			
			member_lookup = MemberLookup (ec.ContainerType, null, base_type, Identifier,
						      AllMemberTypes, AllBindingFlags, loc);
			if (member_lookup == null) {
				Error_MemberLookupFailed (ec.ContainerType, base_type, base_type, Identifier,
					null, AllMemberTypes, AllBindingFlags);
				return null;
			}

			Expression left;
			
			if (ec.IsStatic)
				left = new TypeExpression (base_type, loc);
			else
				left = ec.GetThis (loc);

			MemberExpr me = (MemberExpr) member_lookup;
			me = me.ResolveMemberAccess (ec, left, loc, null);
			if (me == null)
				return null;

			me.IsBase = true;
			if (args != null) {
				args.Resolve (ec);
				me.SetTypeArguments (args);
			}

			return me;
		}

		public override void Emit (EmitContext ec)
		{
			throw new Exception ("Should never be called"); 
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			BaseAccess target = (BaseAccess) t;

			if (args != null)
				target.args = args.Clone ();
		}
	}

	/// <summary>
	///   The base indexer operator
	/// </summary>
	public class BaseIndexerAccess : IndexerAccess {
		public BaseIndexerAccess (ArrayList args, Location loc)
			: base (null, true, loc)
		{
			arguments = new ArrayList ();
			foreach (Expression tmp in args)
				arguments.Add (new Argument (tmp, Argument.AType.Expression));
		}

		protected override bool CommonResolve (EmitContext ec)
		{
			instance_expr = ec.GetThis (loc);

			current_type = ec.ContainerType.BaseType;
			indexer_type = current_type;

			foreach (Argument a in arguments){
				if (!a.Resolve (ec, loc))
					return false;
			}

			return true;
		}
	}
	
	/// <summary>
	///   This class exists solely to pass the Type around and to be a dummy
	///   that can be passed to the conversion functions (this is used by
	///   foreach implementation to typecast the object return value from
	///   get_Current into the proper type.  All code has been generated and
	///   we only care about the side effect conversions to be performed
	///
	///   This is also now used as a placeholder where a no-action expression
	///   is needed (the `New' class).
	/// </summary>
	public class EmptyExpression : Expression {
		public static readonly EmptyExpression Null = new EmptyExpression ();

		public static readonly EmptyExpression OutAccess = new EmptyExpression ();
		public static readonly EmptyExpression LValueMemberAccess = new EmptyExpression ();
		public static readonly EmptyExpression LValueMemberOutAccess = new EmptyExpression ();

		static EmptyExpression temp = new EmptyExpression ();
		public static EmptyExpression Grab ()
		{
			EmptyExpression retval = temp == null ? new EmptyExpression () : temp;
			temp = null;
			return retval;
		}

		public static void Release (EmptyExpression e)
		{
			temp = e;
		}

		// TODO: should be protected
		public EmptyExpression ()
		{
			type = TypeManager.object_type;
			eclass = ExprClass.Value;
			loc = Location.Null;
		}

		public EmptyExpression (Type t)
		{
			type = t;
			eclass = ExprClass.Value;
			loc = Location.Null;
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			// nothing, as we only exist to not do anything.
		}

		//
		// This is just because we might want to reuse this bad boy
		// instead of creating gazillions of EmptyExpressions.
		// (CanImplicitConversion uses it)
		//
		public void SetType (Type t)
		{
			type = t;
		}
	}
	
	//
	// Empty statement expression
	//
	public sealed class EmptyExpressionStatement : ExpressionStatement
	{
		public static readonly EmptyExpressionStatement Instance = new EmptyExpressionStatement ();

		private EmptyExpressionStatement ()
		{
			type = TypeManager.object_type;
			eclass = ExprClass.Value;
			loc = Location.Null;
		}

		public override void EmitStatement (EmitContext ec)
		{
			// Do nothing
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			// Do nothing
		}
	}	

	public class UserCast : Expression {
		MethodInfo method;
		Expression source;
		
		public UserCast (MethodInfo method, Expression source, Location l)
		{
			this.method = method;
			this.source = source;
			type = method.ReturnType;
			eclass = ExprClass.Value;
			loc = l;
		}

		public Expression Source {
			get {
				return source;
			}
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (2);
			args.Add (new Argument (source.CreateExpressionTree (ec)));
			args.Add (new Argument (new TypeOf (new TypeExpression (type, loc), loc)));
			args.Add (new Argument (new Cast (new TypeExpression (typeof (MethodInfo), loc),
				new TypeOfMethod (method, loc))));
			return CreateExpressionFactoryCall ("Convert", args);
		}
			
		public override Expression DoResolve (EmitContext ec)
		{
			//
			// We are born fully resolved
			//
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			source.Emit (ec);
			ec.ig.Emit (OpCodes.Call, method);
		}
	}

	// <summary>
	//   This class is used to "construct" the type during a typecast
	//   operation.  Since the Type.GetType class in .NET can parse
	//   the type specification, we just use this to construct the type
	//   one bit at a time.
	// </summary>
	public class ComposedCast : TypeExpr {
		Expression left;
		string dim;
		
		public ComposedCast (Expression left, string dim)
			: this (left, dim, left.Location)
		{
		}

		public ComposedCast (Expression left, string dim, Location l)
		{
			this.left = left;
			this.dim = dim;
			loc = l;
		}

		public Expression RemoveNullable ()
		{
			if (dim.EndsWith ("?")) {
				dim = dim.Substring (0, dim.Length - 1);
				if (dim.Length == 0)
					return left;
			}

			return this;
		}

		protected override TypeExpr DoResolveAsTypeStep (IResolveContext ec)
		{
			TypeExpr lexpr = left.ResolveAsTypeTerminal (ec, false);
			if (lexpr == null)
				return null;

			Type ltype = lexpr.Type;
			if ((ltype == TypeManager.void_type) && (dim != "*")) {
				Error_VoidInvalidInTheContext (loc);
				return null;
			}

#if GMCS_SOURCE
			if ((dim.Length > 0) && (dim [0] == '?')) {
				TypeExpr nullable = new NullableType (left, loc);
				if (dim.Length > 1)
					nullable = new ComposedCast (nullable, dim.Substring (1), loc);
				return nullable.ResolveAsTypeTerminal (ec, false);
			}
#endif

			if (dim == "*" && !TypeManager.VerifyUnManaged (ltype, loc))
				return null;

			if (dim != "" && dim [0] == '[' &&
			    (ltype == TypeManager.arg_iterator_type || ltype == TypeManager.typed_reference_type)) {
				Report.Error (611, loc, "Array elements cannot be of type `{0}'", TypeManager.CSharpName (ltype));
				return null;
			}

			if (dim != "")
				type = TypeManager.GetConstructedType (ltype, dim);
			else
				type = ltype;

			if (type == null)
				throw new InternalErrorException ("Couldn't create computed type " + ltype + dim);

			if (type.IsPointer && !ec.IsInUnsafeScope){
				UnsafeError (loc);
				return null;
			}

			eclass = ExprClass.Type;
			return this;
		}

		public override string Name {
			get { return left + dim; }
		}

		public override string FullName {
			get { return type.FullName; }
		}

		public override string GetSignatureForError ()
		{
			return left.GetSignatureForError () + dim;
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			ComposedCast target = (ComposedCast) t;

			target.left = left.Clone (clonectx);
		}
	}

	public class FixedBufferPtr : Expression {
		Expression array;

		public FixedBufferPtr (Expression array, Type array_type, Location l)
		{
			this.array = array;
			this.loc = l;

			type = TypeManager.GetPointerType (array_type);
			eclass = ExprClass.Value;
		}

		public override void Emit(EmitContext ec)
		{
			array.Emit (ec);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			//
			// We are born fully resolved
			//
			return this;
		}
	}


	//
	// This class is used to represent the address of an array, used
	// only by the Fixed statement, this generates "&a [0]" construct
	// for fixed (char *pa = a)
	//
	public class ArrayPtr : FixedBufferPtr {
		Type array_type;
		
		public ArrayPtr (Expression array, Type array_type, Location l):
			base (array, array_type, l)
		{
			this.array_type = array_type;
		}

		public override void Emit (EmitContext ec)
		{
			base.Emit (ec);
			
			ILGenerator ig = ec.ig;
			IntLiteral.EmitInt (ig, 0);
			ig.Emit (OpCodes.Ldelema, array_type);
		}
	}

	//
	// Encapsulates a conversion rules required for array indexes
	//
	public class ArrayIndexCast : Expression
	{
		Expression expr;

		public ArrayIndexCast (Expression expr)
		{
			this.expr = expr;
			this.loc = expr.Location;
		}

		public override Expression CreateExpressionTree (EmitContext ec)
		{
			ArrayList args = new ArrayList (2);
			args.Add (new Argument (expr.CreateExpressionTree (ec)));
			args.Add (new Argument (new TypeOf (new TypeExpression (TypeManager.int32_type, loc), loc)));
			return CreateExpressionFactoryCall ("ConvertChecked", args);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			type = expr.Type;
			eclass = expr.eclass;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			expr.Emit (ec);
				
			if (type == TypeManager.int32_type)
				return;

			if (type == TypeManager.uint32_type)
				ec.ig.Emit (OpCodes.Conv_U);
			else if (type == TypeManager.int64_type)
				ec.ig.Emit (OpCodes.Conv_Ovf_I);
			else if (type == TypeManager.uint64_type)
				ec.ig.Emit (OpCodes.Conv_Ovf_I_Un);
			else
				throw new InternalErrorException ("Cannot emit cast to unknown array element type", type);
		}
	}

	//
	// Used by the fixed statement
	//
	public class StringPtr : Expression {
		LocalBuilder b;
		
		public StringPtr (LocalBuilder b, Location l)
		{
			this.b = b;
			eclass = ExprClass.Value;
			type = TypeManager.char_ptr_type;
			loc = l;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			// This should never be invoked, we are born in fully
			// initialized state.

			return this;
		}

		public override void Emit (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			ig.Emit (OpCodes.Ldloc, b);
			ig.Emit (OpCodes.Conv_I);
			ig.Emit (OpCodes.Call, TypeManager.int_get_offset_to_string_data);
			ig.Emit (OpCodes.Add);
		}
	}
	
	//
	// Implements the `stackalloc' keyword
	//
	public class StackAlloc : Expression {
		Type otype;
		Expression t;
		Expression count;
		
		public StackAlloc (Expression type, Expression count, Location l)
		{
			t = type;
			this.count = count;
			loc = l;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			count = count.Resolve (ec);
			if (count == null)
				return null;
			
			if (count.Type != TypeManager.int32_type){
				count = Convert.ImplicitConversionRequired (ec, count, TypeManager.int32_type, loc);
				if (count == null)
					return null;
			}

			Constant c = count as Constant;
			if (c != null && c.IsNegative) {
				Report.Error (247, loc, "Cannot use a negative size with stackalloc");
				return null;
			}

			if (ec.InCatch || ec.InFinally) {
				Error (255, "Cannot use stackalloc in finally or catch");
				return null;
			}

			TypeExpr texpr = t.ResolveAsTypeTerminal (ec, false);
			if (texpr == null)
				return null;

			otype = texpr.Type;

			if (!TypeManager.VerifyUnManaged (otype, loc))
				return null;

			type = TypeManager.GetPointerType (otype);
			eclass = ExprClass.Value;

			return this;
		}

		public override void Emit (EmitContext ec)
		{
			int size = GetTypeSize (otype);
			ILGenerator ig = ec.ig;
				
			if (size == 0)
				ig.Emit (OpCodes.Sizeof, otype);
			else
				IntConstant.EmitInt (ig, size);
			count.Emit (ec);
			ig.Emit (OpCodes.Mul);
			ig.Emit (OpCodes.Localloc);
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			StackAlloc target = (StackAlloc) t;
			target.count = count.Clone (clonectx);
			target.t = t.Clone (clonectx);
		}
	}

	//
	// An object initializer expression
	//
	public class ElementInitializer : Expression
	{
		Expression initializer;
		public readonly string Name;

		public ElementInitializer (string name, Expression initializer, Location loc)
		{
			this.Name = name;
			this.initializer = initializer;
			this.loc = loc;
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			if (initializer == null)
				return;

			ElementInitializer target = (ElementInitializer) t;
			target.initializer = initializer.Clone (clonectx);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (initializer == null)
				return EmptyExpressionStatement.Instance;
			
			MemberExpr element_member = MemberLookupFinal (ec, ec.CurrentInitializerVariable.Type, ec.CurrentInitializerVariable.Type,
				Name, MemberTypes.Field | MemberTypes.Property, BindingFlags.Public | BindingFlags.Instance, loc) as MemberExpr;

			if (element_member == null)
				return null;

			element_member.InstanceExpression = ec.CurrentInitializerVariable;

			if (initializer is CollectionOrObjectInitializers) {
				Expression previous = ec.CurrentInitializerVariable;
				ec.CurrentInitializerVariable = element_member;
				initializer = initializer.Resolve (ec);
				ec.CurrentInitializerVariable = previous;
				return initializer;
			}

			Assign a = new Assign (element_member, initializer, loc);
			if (a.Resolve (ec) == null)
				return null;

			//
			// Ignore field initializers with default value
			//
			Constant c = a.Source as Constant;
			if (c != null && c.IsDefaultInitializer (a.Type) && a.Target.eclass == ExprClass.Variable)
				return EmptyExpressionStatement.Instance;

			return a;
		}

		protected override Expression Error_MemberLookupFailed (MemberInfo[] members)
		{
			MemberInfo member = members [0];
			if (member.MemberType != MemberTypes.Property && member.MemberType != MemberTypes.Field)
				Report.Error (1913, loc, "Member `{0}' cannot be initialized. An object " +
					"initializer may only be used for fields, or properties", TypeManager.GetFullNameSignature (member));
			else
				Report.Error (1914, loc, " Static field or property `{0}' cannot be assigned in an object initializer",
					TypeManager.GetFullNameSignature (member));

			return null;
		}

		public override void Emit (EmitContext ec)
		{
			throw new NotSupportedException ("Should not be reached");
		}
	}
	
	//
	// A collection initializer expression
	//
	public class CollectionElementInitializer : Expression
	{
		public class ElementInitializerArgument : Argument
		{
			public ElementInitializerArgument (Expression e)
				: base (e)
			{
			}
		}

		ArrayList arguments;

		public CollectionElementInitializer (Expression argument)
		{
			arguments = new ArrayList (1);
			arguments.Add (argument);
			this.loc = argument.Location;
		}

		public CollectionElementInitializer (ArrayList arguments, Location loc)
		{
			this.arguments = arguments;
			this.loc = loc;
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			CollectionElementInitializer target = (CollectionElementInitializer) t;
			ArrayList t_arguments = target.arguments = new ArrayList (arguments.Count);
			foreach (Expression e in arguments)
				t_arguments.Add (e.Clone (clonectx));
		}

		public override Expression DoResolve (EmitContext ec)
		{
			// TODO: We should call a constructor which takes element counts argument,
			// for know types like List<T>, Dictionary<T, U>
			
			for (int i = 0; i < arguments.Count; ++i)
				arguments [i] = new ElementInitializerArgument ((Expression)arguments [i]);

			Expression add_method = new Invocation (
				new MemberAccess (ec.CurrentInitializerVariable, "Add", loc),
				arguments);

			add_method = add_method.Resolve (ec);

			return add_method;
		}
		
		public override void Emit (EmitContext ec)
		{
			throw new NotSupportedException ("Should not be reached");
		}
	}
	
	//
	// A block of object or collection initializers
	//
	public class CollectionOrObjectInitializers : ExpressionStatement
	{
		ArrayList initializers;
		
		public static readonly CollectionOrObjectInitializers Empty = 
			new CollectionOrObjectInitializers (new ArrayList (0), Location.Null);

		public CollectionOrObjectInitializers (ArrayList initializers, Location loc)
		{
			this.initializers = initializers;
			this.loc = loc;
		}
		
		public bool IsEmpty {
			get {
				return initializers.Count == 0;
			}
		}

		protected override void CloneTo (CloneContext clonectx, Expression target)
		{
			CollectionOrObjectInitializers t = (CollectionOrObjectInitializers) target;

			t.initializers = new ArrayList (initializers.Count);
			foreach (Expression e in initializers)
				t.initializers.Add (e.Clone (clonectx));
		}
		
		public override Expression DoResolve (EmitContext ec)
		{
			bool is_elements_initialization = false;
			ArrayList element_names = null;
			for (int i = 0; i < initializers.Count; ++i) {
				Expression initializer = (Expression) initializers [i];
				ElementInitializer element_initializer = initializer as ElementInitializer;

				if (i == 0) {
					if (element_initializer != null) {
						is_elements_initialization = true;
						element_names = new ArrayList (initializers.Count);
						element_names.Add (element_initializer.Name);
					} else {
						if (!TypeManager.ImplementsInterface (ec.CurrentInitializerVariable.Type,
							TypeManager.ienumerable_type)) {
							Report.Error (1922, loc, "A field or property `{0}' cannot be initialized with a collection " +
								"object initializer because type `{1}' does not implement `{2}' interface",
								ec.CurrentInitializerVariable.GetSignatureForError (),
								TypeManager.CSharpName (ec.CurrentInitializerVariable.Type),
								TypeManager.CSharpName (TypeManager.ienumerable_type));
							return null;
						}
					}
				} else {
					if (is_elements_initialization == (element_initializer == null)) {
						Report.Error (747, initializer.Location, "Inconsistent `{0}' member declaration",
							is_elements_initialization ? "object initializer" : "collection initializer");
						continue;
					}
					
					if (is_elements_initialization) {
						if (element_names.Contains (element_initializer.Name)) {
							Report.Error (1912, element_initializer.Location,
								"An object initializer includes more than one member `{0}' initialization",
								element_initializer.Name);
						} else {
							element_names.Add (element_initializer.Name);
						}
					}
				}

				Expression e = initializer.Resolve (ec);
				if (e == EmptyExpressionStatement.Instance)
					initializers.RemoveAt (i--);
				else
					initializers [i] = e;
			}

			type = typeof (CollectionOrObjectInitializers);
			eclass = ExprClass.Variable;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			EmitStatement (ec);
		}

		public override void EmitStatement (EmitContext ec)
		{
			foreach (ExpressionStatement e in initializers)
				e.EmitStatement (ec);
		}
	}
	
	//
	// New expression with element/object initializers
	//
	public class NewInitialize : New
	{
		//
		// This class serves as a proxy for variable initializer target instances.
		// A real variable is assigned later when we resolve left side of an
		// assignment
		//
		sealed class InitializerTargetExpression : Expression, IMemoryLocation
		{
			NewInitialize new_instance;

			public InitializerTargetExpression (NewInitialize newInstance)
			{
				this.type = newInstance.type;
				this.loc = newInstance.loc;
				this.eclass = newInstance.eclass;
				this.new_instance = newInstance;
			}

			public override Expression DoResolve (EmitContext ec)
			{
				return this;
			}

			public override Expression DoResolveLValue (EmitContext ec, Expression right_side)
			{
				return this;
			}

			public override void Emit (EmitContext ec)
			{
				new_instance.value_target.Emit (ec);
			}

			#region IMemoryLocation Members

			public void AddressOf (EmitContext ec, AddressOp mode)
			{
				((IMemoryLocation)new_instance.value_target).AddressOf (ec, mode);
			}

			#endregion
		}

		CollectionOrObjectInitializers initializers;

		public NewInitialize (Expression requested_type, ArrayList arguments, CollectionOrObjectInitializers initializers, Location l)
			: base (requested_type, arguments, l)
		{
			this.initializers = initializers;
		}

		protected override void CloneTo (CloneContext clonectx, Expression t)
		{
			base.CloneTo (clonectx, t);

			NewInitialize target = (NewInitialize) t;
			target.initializers = (CollectionOrObjectInitializers) initializers.Clone (clonectx);
		}

		public override Expression DoResolve (EmitContext ec)
		{
			if (eclass != ExprClass.Invalid)
				return this;
			
			Expression e = base.DoResolve (ec);
			if (type == null)
				return null;

			// Empty initializer can be optimized to simple new
			if (initializers.IsEmpty)
				return e;

			Expression previous = ec.CurrentInitializerVariable;
			ec.CurrentInitializerVariable = new InitializerTargetExpression (this);
			initializers.Resolve (ec);
			ec.CurrentInitializerVariable = previous;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			base.Emit (ec);

			//
			// If target is a value, let's use it
			//
			VariableReference variable = value_target as VariableReference;
			if (variable != null) {
				if (variable.IsRef)
					StoreFromPtr (ec.ig, type);
				else
					variable.Variable.EmitAssign (ec);
			} else {
				if (value_target == null || value_target_set)
					value_target = new LocalTemporary (type);

				((LocalTemporary) value_target).Store (ec);
			}

			initializers.Emit (ec);

			if (variable == null)
				value_target.Emit (ec);
		}

		public override void EmitStatement (EmitContext ec)
		{
			if (initializers.IsEmpty) {
				base.EmitStatement (ec);
				return;
			}

			base.Emit (ec);

			if (value_target == null) {
				LocalTemporary variable = new LocalTemporary (type);
				variable.Store (ec);
				value_target = variable;
			}

			initializers.EmitStatement (ec);
		}

		public override bool HasInitializer {
			get {
				return !initializers.IsEmpty;
			}
		}
	}

	public class AnonymousTypeDeclaration : Expression
	{
		ArrayList parameters;
		readonly TypeContainer parent;
		static readonly ArrayList EmptyParameters = new ArrayList (0);

		public AnonymousTypeDeclaration (ArrayList parameters, TypeContainer parent, Location loc)
		{
			this.parameters = parameters;
			this.parent = parent;
			this.loc = loc;
		}

		protected override void CloneTo (CloneContext clonectx, Expression target)
		{
			if (parameters == null)
				return;

			AnonymousTypeDeclaration t = (AnonymousTypeDeclaration) target;
			t.parameters = new ArrayList (parameters.Count);
			foreach (AnonymousTypeParameter atp in parameters)
				t.parameters.Add (atp.Clone (clonectx));
		}

		AnonymousTypeClass CreateAnonymousType (ArrayList parameters)
		{
			AnonymousTypeClass type = RootContext.ToplevelTypes.GetAnonymousType (parameters);
			if (type != null)
				return type;

			type = AnonymousTypeClass.Create (parent, parameters, loc);
			if (type == null)
				return null;

			type.DefineType ();
			type.DefineMembers ();
			type.Define ();
			type.EmitType ();

			RootContext.ToplevelTypes.AddAnonymousType (type);
			return type;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			AnonymousTypeClass anonymous_type;

			if (parameters == null) {
				anonymous_type = CreateAnonymousType (EmptyParameters);
				return new New (new TypeExpression (anonymous_type.TypeBuilder, loc),
					null, loc).Resolve (ec);
			}

			bool error = false;
			ArrayList arguments = new ArrayList (parameters.Count);
			TypeExpression [] t_args = new TypeExpression [parameters.Count];
			for (int i = 0; i < parameters.Count; ++i) {
				Expression e = ((AnonymousTypeParameter) parameters [i]).Resolve (ec);
				if (e == null) {
					error = true;
					continue;
				}

				arguments.Add (new Argument (e));
				t_args [i] = new TypeExpression (e.Type, e.Location);
			}

			if (error)
				return null;

			anonymous_type = CreateAnonymousType (parameters);
			if (anonymous_type == null)
				return null;

			ConstructedType te = new ConstructedType (anonymous_type.TypeBuilder,
				new TypeArguments (loc, t_args), loc);

			return new New (te, arguments, loc).Resolve (ec);
		}

		public override void Emit (EmitContext ec)
		{
			throw new InternalErrorException ("Should not be reached");
		}
	}

	public class AnonymousTypeParameter : Expression
	{
		public readonly string Name;
		Expression initializer;

		public AnonymousTypeParameter (Expression initializer, string name, Location loc)
		{
			this.Name = name;
			this.loc = loc;
			this.initializer = initializer;
		}
		
		public AnonymousTypeParameter (Parameter parameter)
		{
			this.Name = parameter.Name;
			this.loc = parameter.Location;
			this.initializer = new SimpleName (Name, loc);
		}		

		protected override void CloneTo (CloneContext clonectx, Expression target)
		{
			AnonymousTypeParameter t = (AnonymousTypeParameter) target;
			t.initializer = initializer.Clone (clonectx);
		}

		public override bool Equals (object o)
		{
			AnonymousTypeParameter other = o as AnonymousTypeParameter;
			return other != null && Name == other.Name;
		}

		public override int GetHashCode ()
		{
			return Name.GetHashCode ();
		}

		public override Expression DoResolve (EmitContext ec)
		{
			Expression e = initializer.Resolve (ec);
			if (e == null)
				return null;

			type = e.Type;
			if (type == TypeManager.void_type || type == TypeManager.null_type ||
				type == TypeManager.anonymous_method_type || type.IsPointer) {
				Error_InvalidInitializer (e);
				return null;
			}

			return e;
		}

		protected virtual void Error_InvalidInitializer (Expression initializer)
		{
			Report.Error (828, loc, "An anonymous type property `{0}' cannot be initialized with `{1}'",
				Name, initializer.GetSignatureForError ());
		}

		public override void Emit (EmitContext ec)
		{
			throw new InternalErrorException ("Should not be reached");
		}
	}
}
