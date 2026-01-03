using Mono.Cecil;
using System.Linq;
using System.Reflection;

namespace PascalABCCompiler
{
	internal static partial class CecilExt
	{
		public static PropertyReference ImportReference(this ModuleDefinition module, PropertyInfo propInfo)
		{
			return module.ImportReference(propInfo.DeclaringType).Resolve()
				.Properties
				.Single(item => item.Name == propInfo.Name);
		}
	}
}
