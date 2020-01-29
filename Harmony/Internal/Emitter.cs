using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace HarmonyLib
{
	internal class LeaveTry
	{
		public override string ToString()
		{
			return "(autogenerated)";
		}
	}

	static class OpCodeExtension
	{
		internal static int SizeOffset(this OpCode code)
		{
			return code.Size == 1 ? 1 : 2;
		}
	}

	internal class Emitter
	{
		static readonly GetterHandler<ILGenerator, LocalBuilder[]> localsGetter = FastAccess.CreateFieldGetter<ILGenerator, LocalBuilder[]>("locals");

		readonly ILGenerator il;
		int offset;

		internal Emitter(ILGenerator il)
		{
			this.il = il;
			offset = 0;
		}

		internal static string CodePos(int offset)
		{
			return string.Format("L_{0:x4}: ", offset);
		}

		internal string CodePos()
		{
			return CodePos(offset);
		}

		internal void LogComment(string comment)
		{
			var str = string.Format("{0}// {1}", CodePos(), comment);
			FileLog.LogBuffered(str);
		}

		internal void LogIL(OpCode opcode)
		{
			if (Harmony.DEBUG)
				FileLog.LogBuffered(string.Format("{0}{1}", CodePos(), opcode));

			offset += opcode.SizeOffset();
		}

		internal void LogIL(OpCode opcode, object arg, string extra = null)
		{
			if (Harmony.DEBUG)
			{
				var argStr = FormatArgument(arg, extra);
				var space = argStr.Length > 0 ? " " : "";
				var opcodeName = opcode.ToString();
				if (opcodeName.StartsWith("br") && opcodeName != "break") opcodeName += " =>";
				opcodeName = opcodeName.PadRight(10);
				FileLog.LogBuffered(string.Format("{0}{1}{2}{3}", CodePos(), opcodeName, space, argStr));
			}

			offset += opcode.SizeOffset();
			var isSingleByte = OpCodes.TakesSingleByteArgument(opcode);

			if (arg is LocalBuilder)
			{
				if (opcode.OperandType != OperandType.InlineNone)
					offset += isSingleByte ? 1 : 2;
				return;
			}
			if (arg is Label[])
			{
				offset += 4 + ((Label[])arg).Length * 4;
				return;
			}
			if (arg is Label)
			{
				offset += isSingleByte ? 1 : 4;
				return;
			}
			if (arg is byte || arg is sbyte)
			{
				offset += 1;
				return;
			}
			if (arg is short)
			{
				offset += 2;
				return;
			}
			if (arg is long || arg is double)
			{
				offset += 8;
				return;
			}
			offset += 4;
		}

		internal LocalBuilder[] AllLocalVariables()
		{
			return localsGetter != null ? localsGetter(il) : new LocalBuilder[0];
		}

		internal static void LogLocalVariable(LocalBuilder variable)
		{
			if (Harmony.DEBUG)
			{
				var str = string.Format("{0}Local var {1}: {2}{3}", CodePos(0), variable.LocalIndex, variable.LocalType.FullName, variable.IsPinned ? "(pinned)" : "");
				FileLog.LogBuffered(str);
			}
		}

		internal static string FormatArgument(object argument, string extra = null)
		{
			if (argument == null) return "NULL";
			var type = argument.GetType();

			var method = argument as MethodInfo;
			if (method != null)
				return ((MethodInfo)argument).FullDescription() + (extra != null ? " " + extra : "");

			if (type == typeof(string))
				return $"\"{argument}\"";
			if (type == typeof(Label))
				return $"Label{((Label)argument).GetHashCode()}";
			if (type == typeof(Label[]))
				return $"Labels{string.Join(",", ((Label[])argument).Select(l => l.GetHashCode().ToString()).ToArray())}";
			if (type == typeof(LocalBuilder))
				return $"{((LocalBuilder)argument).LocalIndex} ({((LocalBuilder)argument).LocalType})";

			return argument.ToString().Trim();
		}

		internal void MarkLabel(Label label)
		{
			if (Harmony.DEBUG) FileLog.LogBuffered(CodePos() + FormatArgument(label));
			il.MarkLabel(label);
		}

		internal void MarkBlockBefore(ExceptionBlock block, out Label? label)
		{
			label = null;
			switch (block.blockType)
			{
				case ExceptionBlockType.BeginExceptionBlock:
					if (Harmony.DEBUG)
					{
						FileLog.LogBuffered(".try");
						FileLog.LogBuffered("{");
						FileLog.ChangeIndent(1);
					}
					label = il.BeginExceptionBlock();
					return;

				case ExceptionBlockType.BeginCatchBlock:
					if (Harmony.DEBUG)
					{
						// fake log a LEAVE code since BeginCatchBlock() does add it
						LogIL(OpCodes.Leave, new LeaveTry());

						FileLog.ChangeIndent(-1);
						FileLog.LogBuffered("} // end try");

						FileLog.LogBuffered($".catch {block.catchType}");
						FileLog.LogBuffered("{");
						FileLog.ChangeIndent(1);
					}
					il.BeginCatchBlock(block.catchType);
					return;

				case ExceptionBlockType.BeginExceptFilterBlock:
					if (Harmony.DEBUG)
					{
						// fake log a LEAVE code since BeginCatchBlock() does add it
						LogIL(OpCodes.Leave, new LeaveTry());

						FileLog.ChangeIndent(-1);
						FileLog.LogBuffered("} // end try");

						FileLog.LogBuffered(".filter");
						FileLog.LogBuffered("{");
						FileLog.ChangeIndent(1);
					}
					il.BeginExceptFilterBlock();
					return;

				case ExceptionBlockType.BeginFaultBlock:
					if (Harmony.DEBUG)
					{
						// fake log a LEAVE code since BeginCatchBlock() does add it
						LogIL(OpCodes.Leave, new LeaveTry());

						FileLog.ChangeIndent(-1);
						FileLog.LogBuffered("} // end try");

						FileLog.LogBuffered(".fault");
						FileLog.LogBuffered("{");
						FileLog.ChangeIndent(1);
					}
					il.BeginFaultBlock();
					return;

				case ExceptionBlockType.BeginFinallyBlock:
					if (Harmony.DEBUG)
					{
						// fake log a LEAVE code since BeginCatchBlock() does add it
						LogIL(OpCodes.Leave, new LeaveTry());

						FileLog.ChangeIndent(-1);
						FileLog.LogBuffered("} // end try");

						FileLog.LogBuffered(".finally");
						FileLog.LogBuffered("{");
						FileLog.ChangeIndent(1);
					}
					il.BeginFinallyBlock();
					return;
			}
		}

		internal void MarkBlockAfter(ExceptionBlock block)
		{
			if (block.blockType == ExceptionBlockType.EndExceptionBlock)
			{
				if (Harmony.DEBUG)
				{
					// fake log a LEAVE code since BeginCatchBlock() does add it
					LogIL(OpCodes.Leave, new LeaveTry());

					FileLog.ChangeIndent(-1);
					FileLog.LogBuffered("} // end handler");
				}
				il.EndExceptionBlock();
			}
		}

		internal void Emit(OpCode opcode)
		{
			LogIL(opcode);
			il.Emit(opcode);
		}

		internal void Emit(OpCode opcode, LocalBuilder local)
		{
			LogIL(opcode, local);
			il.Emit(opcode, local);
		}

		internal void Emit(OpCode opcode, FieldInfo field)
		{
			LogIL(opcode, field);
			il.Emit(opcode, field);
		}

		internal void Emit(OpCode opcode, Label[] labels)
		{
			LogIL(opcode, labels);
			il.Emit(opcode, labels);
		}

		internal void Emit(OpCode opcode, Label label)
		{
			LogIL(opcode, label);
			il.Emit(opcode, label);
		}

		internal void Emit(OpCode opcode, string str)
		{
			LogIL(opcode, str);
			il.Emit(opcode, str);
		}

		internal void Emit(OpCode opcode, float arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, byte arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, sbyte arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, double arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, int arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, MethodInfo meth)
		{
			if (opcode.Equals(OpCodes.Call) || opcode.Equals(OpCodes.Callvirt) || opcode.Equals(OpCodes.Newobj))
			{
				EmitCall(opcode, meth, null);
				return;
			}

			LogIL(opcode, meth);
			il.Emit(opcode, meth);
		}

		internal void Emit(OpCode opcode, short arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, SignatureHelper signature)
		{
			LogIL(opcode, signature);
			il.Emit(opcode, signature);
		}

		internal void Emit(OpCode opcode, ConstructorInfo con)
		{
			LogIL(opcode, con);
			il.Emit(opcode, con);
		}

		internal void Emit(OpCode opcode, Type cls)
		{
			LogIL(opcode, cls);
			il.Emit(opcode, cls);
		}

		internal void Emit(OpCode opcode, long arg)
		{
			LogIL(opcode, arg);
			il.Emit(opcode, arg);
		}

		internal void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[] optionalParameterTypes)
		{
			var extra = optionalParameterTypes != null && optionalParameterTypes.Length > 0 ? optionalParameterTypes.Description() : null;
			LogIL(opcode, methodInfo, extra);
			il.EmitCall(opcode, methodInfo, optionalParameterTypes);
		}

#if NETSTANDARD2_0 || NETCOREAPP2_0
#else
		internal void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type returnType, Type[] parameterTypes)
		{
			var extra = returnType.FullName + " " + parameterTypes.Description();
			LogIL(opcode, unmanagedCallConv, extra);
			il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
		}
#endif

		internal void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, Type[] optionalParameterTypes)
		{
			var extra = returnType.FullName + " " + parameterTypes.Description() + " " + optionalParameterTypes.Description();
			LogIL(opcode, callingConvention, extra);
			il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
		}
	}
}