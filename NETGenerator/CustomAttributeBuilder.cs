using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace PascalABCCompiler.NETGenerator
{
	internal class CustomAttributeBuilder
	{
		private CustomAttribute result;

		public CustomAttributeBuilder(MethodReference ctor, byte[] blob = null)
		{
			if (blob == null)
				result = new CustomAttribute(ctor);
			else
				result = new CustomAttribute(ctor, blob);
		}

		public static CustomAttributeBuilder GetInstance(MethodReference ctor, byte[] blob = null)
			=> new CustomAttributeBuilder(ctor, blob);

		public CustomAttributeBuilder AddConstructorArgs(IList<object> args)
		{
			for (var i = 0; i < result.Constructor.Parameters.Count(); i++)
			{
				var param = result.Constructor.Parameters[i];
				result.ConstructorArguments.Add
				(
					new CustomAttributeArgument(param.ParameterType, args[i])
				);
			}

			return this;
		}

		public CustomAttributeBuilder AddPropertyArgs(IList<PropertyReference> props, IList<object> args)
		{
			for (var i = 0; i < props.Count; i++)
			{
				result.Properties.Add
				(
					new CustomAttributeNamedArgument
					(
						props[i].Name,
						new CustomAttributeArgument(props[i].PropertyType, args[i])
					)
				);
			}

			return this;
		}

		public CustomAttributeBuilder AddFieldArgs(IList<FieldReference> props, IList<object> args)
		{
			for (var i = 0; i < props.Count; i++)
			{
				result.Properties.Add
				(
					new CustomAttributeNamedArgument
					(
						props[i].Name,
						new CustomAttributeArgument(props[i].FieldType, args[i])
					)
				);
			}

			return this;
		}

		public CustomAttribute Build() => result;
	}
}
