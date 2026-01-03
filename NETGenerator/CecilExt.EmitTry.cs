using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PascalABCCompiler
{
	internal class ExceptionBlock
	{
		private readonly Instruction tryStart;

		private readonly List<Tuple<Instruction, TypeReference>> catchList = new List<Tuple<Instruction, TypeReference>>();
		private Instruction finallyAfter;

		public bool IsCatchBlock { get; private set; }

		public bool IsFinallyBlock { get; private set; }

		public Instruction BlockEnd { get; } = Instruction.Create(OpCodes.Nop);

		public void AddCatch(Instruction startsAfter, TypeReference exceptionType)
		{
			if (IsFinallyBlock)
				throw null;

			IsCatchBlock = true;

			catchList.Add( Tuple.Create(startsAfter, exceptionType) );
		}

		public void AddFinally(Instruction startsAfter)
		{
			if (IsCatchBlock)
				throw null;

			IsFinallyBlock = true;

			finallyAfter = startsAfter;
		}

		public List<ExceptionHandler> ToHandlers()
		{
			Instruction tryEnd;

			Instruction handlerEnd;
			Instruction handlerStart;

			if (IsFinallyBlock)
			{
				tryEnd = finallyAfter.Next;

				handlerStart = tryEnd;
				handlerEnd = BlockEnd;

				var handler = new ExceptionHandler(ExceptionHandlerType.Finally)
				{
					TryStart = tryStart, TryEnd = tryEnd,
					HandlerStart = handlerStart, HandlerEnd = handlerEnd
				};

				return new List<ExceptionHandler> { handler };
			}

			var result = new List<ExceptionHandler>(catchList.Count);

			tryEnd = catchList[0].Item1.Next;

			for (var i = 0;  i < catchList.Count; i++)
			{
				handlerStart = catchList[i].Item1.Next;

				if (i == catchList.Count - 1)
					handlerEnd = BlockEnd;
				else
					handlerEnd = catchList[i+1].Item1.Next;

				result.Add
				(
					new ExceptionHandler(ExceptionHandlerType.Catch)
					{
						TryStart = tryStart, TryEnd = tryEnd,
						HandlerStart = handlerStart, HandlerEnd = handlerEnd,
						CatchType = catchList[i].Item2
					}
				);
			}

			return result;
		}

		public ExceptionBlock(Instruction tryStart)
		{
			this.tryStart = tryStart;
		}
	}

	internal static partial class CecilExt
	{
		private static Dictionary<ILProcessor, Stack<ExceptionBlock>> renameIt = new Dictionary<ILProcessor, Stack<ExceptionBlock>>();

		public static Instruction BeginExceptionBlock(this ILProcessor il)
		{
			var label = il.Create(OpCodes.Nop);
			il.Append(label);

			var v = new ExceptionBlock(label);

			if (!renameIt.ContainsKey(il))
				renameIt[il] = new Stack<ExceptionBlock>();

			renameIt[il].Push(v);

			return v.BlockEnd;
		}

		public static void BeginCatchBlock(this ILProcessor il, TypeReference exceptionType)
		{
			var v = renameIt[il].Peek();

			il.Emit(OpCodes.Leave, v.BlockEnd);

			v.AddCatch(il.Body.Instructions.Last(), exceptionType);
		}

		public static void BeginFinallyBlock(this ILProcessor il)
		{
			var v = renameIt[il].Peek();

			il.Emit(OpCodes.Leave, v.BlockEnd);

			v.AddFinally( il.Body.Instructions.Last() );
		}

		public static void EndExceptionBlock(this ILProcessor il)
		{
			var v = renameIt[il].Pop();

			if (v.IsCatchBlock)
				il.Emit(OpCodes.Leave, v.BlockEnd);
			else
				il.Emit(OpCodes.Endfinally);

			il.Append(v.BlockEnd);

			var handlers = v.ToHandlers();

			foreach (var handler in handlers)
				il.Body.ExceptionHandlers.Add(handler);

		}
	}
}
