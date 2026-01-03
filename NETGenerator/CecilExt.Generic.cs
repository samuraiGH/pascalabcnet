using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;

namespace PascalABCCompiler.NETGenerator
{
	internal static partial class CecilExt
	{
		public static MethodReference AsMemberOf(this MethodReference orig, GenericInstanceType instance)
		{
			TypeReference newReturnType;

			// если возвращаемое значение было именем шаблона, определённым в классе
			// то находим подставленный тип в instance
			//if (orig.ReturnType.ContainsGenericParameter)
				//newReturnType = SubstituteTypes(orig.ReturnType, instance.GenericArguments);
			//else
				newReturnType = orig.ReturnType;

			var result = new MethodReference(orig.Name, newReturnType, instance)
			{
				HasThis = orig.HasThis,
				CallingConvention = orig.CallingConvention,
				ExplicitThis = orig.ExplicitThis
			};

			foreach (var origParam in orig.Parameters)
			{
				ParameterDefinition newParam;

				// если тип параметра был именем шаблона, определённым в классе
				// то находим подставленный тип в instance
				//if (origParam.ParameterType.ContainsGenericParameter)
				//{
				//	var newParameterType = SubstituteTypes(origParam.ParameterType, instance.GenericArguments);
				//	newParam = new ParameterDefinition(origParam.Name, origParam.Attributes, newParameterType);
				//}
				//else
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

			//if (orig.FieldType.ContainsGenericParameter)
				//newFieldType = SubstituteTypes(orig.FieldType, instance.GenericArguments);
			//else
				newFieldType = orig.FieldType;

			return new FieldReference(orig.Name, newFieldType, instance);
		}

		private static TypeReference SubstituteTypes(TypeReference orig, IList<TypeReference> genericArgs)
		{
			if (!orig.ContainsGenericParameter)
				throw new InvalidOperationException();

			if (orig.IsGenericParameter)
			{
				var param = (GenericParameter)orig;

				if (param.Type == GenericParameterType.Type)
					return genericArgs[param.Position];
				else
					return orig;
			}

			//TODO учесть больше типов-спецификаций?
			if (orig.IsByReference)
			{
				var typeSpec = (TypeSpecification)orig;
				return SubstituteTypes(typeSpec.ElementType, genericArgs).MakeByReferenceType();
			}

			if (orig.IsArray)
			{
				var typeSpec = (ArrayType)orig;
				return SubstituteTypes(typeSpec.ElementType, genericArgs).MakeArrayType(typeSpec.Rank);
			}

			var instance = (GenericInstanceType)orig;

			var actualTypes = new TypeReference[instance.GenericArguments.Count];

			for (var i = 0; i < actualTypes.Length; i++)
			{
				if (instance.GenericArguments[i].ContainsGenericParameter)
					actualTypes[i] = SubstituteTypes(instance.GenericArguments[i], genericArgs);
				else
					actualTypes[i] = instance.GenericArguments[i];
			}

			return instance.ElementType.MakeGenericInstanceType(actualTypes);
		}
	}
}
