using Mono.Cecil;
using System.Linq;

namespace PascalABCCompiler.NETGenerator
{
	internal static class CecilExt
	{
		public static MethodReference AsMemberOf(this MethodReference orig, GenericInstanceType instance)
		{
			TypeReference newReturnType;
			var possiblyGenericParam = orig.ReturnType as GenericParameter;

			// если возвращаемое значение было именем шаблона, определённым в классе
			// то находим подставленный тип в instance
			if (orig.ReturnType.IsGenericParameter && possiblyGenericParam.Type == GenericParameterType.Type)
				newReturnType = possiblyGenericParam.FindArgument(instance);
			else
				newReturnType = orig.ReturnType;

			var result = new MethodReference(orig.Name, newReturnType, instance)
			{
				HasThis = orig.HasThis,
				CallingConvention = orig.CallingConvention,
				ExplicitThis = orig.ExplicitThis
			};

			foreach (var origParam in orig.Parameters)
			{
				possiblyGenericParam = origParam.ParameterType as GenericParameter;

				ParameterDefinition newParam;

				// если тип параметра был именем шаблона, определённым в классе
				// то находим подставленный тип в instance
				if (origParam.ParameterType.IsGenericParameter && possiblyGenericParam.Type == GenericParameterType.Type)
				{
					var genericArg = possiblyGenericParam.FindArgument(instance);

					newParam = new ParameterDefinition(origParam.Name, origParam.Attributes, genericArg);
				}
				else
					newParam = origParam;

				result.Parameters.Add(newParam);
			}
			
			foreach (var item in orig.GenericParameters)
				result.GenericParameters.Add(item);
			
			return result;
		}

		public static FieldReference AsMemberOf(this FieldReference orig, GenericInstanceType instance)
		{
			TypeReference newFieldType;

			if (orig.FieldType.IsGenericParameter)
			{
				var param = (GenericParameter)orig.FieldType;
				newFieldType = param.FindArgument(instance);
			}
			else
				newFieldType = orig.FieldType;

			return new FieldReference(orig.Name, newFieldType, instance);
		}


		public static TypeReference FindArgument(this GenericParameter param, GenericInstanceType instance)
		{
			var ind = instance.GenericParameters.IndexOf(param);

			if (ind == -1)
				throw null;

			return instance.GenericArguments[ind];
		}
	}
}
