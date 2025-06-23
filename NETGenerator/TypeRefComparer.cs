using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace PascalABCCompiler
{
	internal class TypeRefComparer : IEqualityComparer<TypeReference>
	{
		public bool Equals(TypeReference x, TypeReference y)
		{
			return x.FullName == y.FullName;
		}

		public int GetHashCode(TypeReference obj)
		{
			return 0;
		}
	}
}
