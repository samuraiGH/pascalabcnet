//using Mono.Cecil;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace PascalABCCompiler
//{
//	internal static class Aboba
//	{
//		public static void Go(AssemblyDefinition asm, AssemblyNameDefinition asmName)
//		{
//			var mod = asm.MainModule;

//			mod.Types[1].Methods[0].Body.GetILProcessor().Clear();
//			mod.Types[1].Methods[2].Body.GetILProcessor().Clear();
//			mod.Types[1].Methods.RemoveAt(4);
//			mod.Types[1].Methods.RemoveAt(3);
//			mod.Types[1].Methods.RemoveAt(2);
//			mod.Types[1].Methods.RemoveAt(1);

//			var temp = mod.Types[37];

//			temp.Fields.Clear();

//			//temp.Methods[21].PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CharSetAnsi, "AllocConsole", new ModuleReference("kernel32.dll"));

//			foreach (var typ in mod.Types)
//				foreach (var meth in typ.Methods)
//					meth.Body?.GetILProcessor().Clear();

//			//var keep = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 13, 35, 36, 38 };
//			var keep = new int[] {0, 1, 5, 37, 38 };
//			for (var i = 42; i >= 0; i--)
//			{
//				if (!keep.Contains(i))
//					mod.Types.RemoveAt(i);
//			}

//			asm.Write(asmName.Name + ".exe");
//		}
//	}
//}
