// Copyright (c) Ivan Bondarev, Stanislav Mikhalkovich (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)
using PascalABCCompiler.TreeRealization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;

using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace PascalABCCompiler.NETGenerator
{
    // TODO многие из этих типов сравниваются через ==
    // нужно проверить, что сравнение заменено на .FullName
    public class TypeFactory
    {
        public static Type AttributeType = typeof(Attribute);
        public static Type DefaultMemberAttributeType = typeof(DefaultMemberAttribute);
        public static Type ConditionalAttributeType = typeof(System.Diagnostics.ConditionalAttribute);

        public static Type ExceptionType = typeof(Exception);
        public static Type VoidType = typeof(void);
        public static Type StringType = typeof(string);
        public static Type ObjectType = typeof(object);
        public static Type MonitorType = typeof(System.Threading.Monitor);
        public static Type IntPtrType = typeof(System.IntPtr);
        public static Type UIntPtrType = typeof(UIntPtr);
        public static Type ArrayType = typeof(System.Array);
        public static Type MulticastDelegateType = typeof(MulticastDelegate);
        public static Type EnumType = typeof(Enum);
        public static Type ExtensionAttributeType = typeof(System.Runtime.CompilerServices.ExtensionAttribute);
        public static Type ConvertType = typeof(Convert);

        //primitive
        public static Type BoolType = typeof(Boolean);
        public static Type SByteType = typeof(SByte);
        public static Type ByteType = typeof(Byte);
        public static Type CharType = typeof(Char);
        public static Type Int16Type = typeof(Int16);
        public static Type Int32Type = typeof(Int32);
        public static Type Int64Type = typeof(Int64);
        public static Type UInt16Type = typeof(UInt16);
        public static Type UInt32Type = typeof(UInt32);
        public static Type UInt64Type = typeof(UInt64);
        public static Type SingleType = typeof(Single);
        public static Type DoubleType = typeof(Double);
        public static Type GCHandleType = typeof(GCHandle);
        public static Type MarshalType = typeof(Marshal);
        public static Type TypeType = typeof(Type);
        public static Type ValueType = typeof(ValueType);
        public static Type IEnumerableType = typeof(System.Collections.IEnumerable);
        public static Type IEnumeratorType = typeof(System.Collections.IEnumerator);
        public static Type IDisposableType = typeof(IDisposable);
        public static Type IEnumerableGenericType = typeof(System.Collections.Generic.IEnumerable<>);
        public static Type IEnumeratorGenericType = typeof(System.Collections.Generic.IEnumerator<>);

        private static HashSet<Mono.Cecil.TypeReference> types;
        private static Dictionary<Mono.Cecil.TypeReference, int> sizes;

        public static MethodInfo ArrayCopyMethod;
        public static MethodInfo ArrayLengthGetMethod;
        public static MethodInfo GetTypeFromHandleMethod;
        public static MethodInfo ResizeMethod;
        public static MethodInfo GCHandleFreeMethod;
        public static MethodInfo StringCopyMethod;
        public static MethodInfo StringNullOrEmptyMethod;
        public static MethodInfo GCHandleAlloc;
        public static MethodInfo GCHandleAllocPinned;
        public static MethodInfo OffsetToStringDataProperty;
        public static MethodInfo StringLengthMethod;
        public static MethodInfo CharToString;
        public static MethodInfo MathMinMethod;
        public static MethodInfo MarshalAllocHGlobalMethod;
        public static MethodInfo MarshalFreeHGlobalMethod;
        public static MethodInfo MarshalCopyMethod;
        public static MethodInfo MarshalSizeOfMethod;
        public static MethodInfo NullableHasValueGetMethod;
        public static MethodInfo NullableGetValueOrDefaultMethod;
        public static MethodInfo EnvironmentIs64BitProcessGetMethod;
        public static MethodInfo ActivatorCreateInstanceMethod;
        public static MethodInfo IEnumerableGenericGetEnumeratorMethod;
        public static MethodInfo IEnumerableGetEnumeratorMethod;
        public static MethodInfo IEnumeratorMoveNextMethod;
        public static MethodInfo MonitorEnterMethod;
        public static MethodInfo MonitorExitMethod;
        public static MethodInfo ConvertToByteMethod;
        public static MethodInfo ConvertToSByteMethod;
        public static MethodInfo ConvertToInt16Method;
        public static MethodInfo ConvertToUInt16Method;
        public static MethodInfo ConvertToInt32Method;
        public static MethodInfo ConvertToUInt32Method;
        public static MethodInfo ConvertToInt64Method;
        public static MethodInfo ConvertToUInt64Method;
        public static MethodInfo ConvertToCharMethod;
        public static MethodInfo ConvertToBooleanMethod;
        public static MethodInfo ConvertToDoubleMethod;
        public static MethodInfo ConvertToSingleMethod;
        public static MethodInfo IDisposableDisposeMethod;

        public static PropertyInfo SecurityRulesAttributeSkipVerificationInFullTrustProperty;

        public static ConstructorInfo IndexOutOfRangeCtor;
        public static ConstructorInfo AttributeCtor;
        public static ConstructorInfo ParamArrayAttributeCtor;
        public static ConstructorInfo DebuggableAttributeCtor;
        public static ConstructorInfo AssemblyKeyFileAttributeCtor;
        public static ConstructorInfo AssemblyDelaySignAttributeCtor;
        public static ConstructorInfo TargetFrameworkAttributeCtor;
        public static ConstructorInfo SecurityRulesAttributeCtor;
        public static ConstructorInfo STAThreadAttributeCtor;
        public static ConstructorInfo CompilationRelaxationsAttributeCtor;
        public static ConstructorInfo AssemblyTitleAttributeCtor;
        public static ConstructorInfo AssemblyDescriptionAttributeCtor;

        public static void Init(Mono.Cecil.ModuleDefinition module)
        {
            var comparer = new TypeRefComparer();

            types = new HashSet<Mono.Cecil.TypeReference>(comparer)
            {
                module.TypeSystem.Boolean, module.TypeSystem.SByte, module.TypeSystem.Byte, module.TypeSystem.Char,
                module.TypeSystem.Int16, module.TypeSystem.Int32, module.TypeSystem.Int64,
                module.TypeSystem.UInt16, module.TypeSystem.UInt32, module.TypeSystem.UInt64,
                module.TypeSystem.Single, module.TypeSystem.Double
            };
            
            sizes = new Dictionary<Mono.Cecil.TypeReference, int>(comparer)
            {
                [module.TypeSystem.Boolean] = sizeof(Boolean),
                [module.TypeSystem.SByte] = sizeof(SByte),
                [module.TypeSystem.Byte] = sizeof(Byte),
                [module.TypeSystem.Char] = sizeof(Char),
                [module.TypeSystem.Int16] = sizeof(Int16),
                [module.TypeSystem.Int32] = sizeof(Int32),
                [module.TypeSystem.Int64] = sizeof(Int64),
                [module.TypeSystem.UInt16] = sizeof(UInt16),
                [module.TypeSystem.UInt32] = sizeof(UInt32),
                [module.TypeSystem.UInt64] = sizeof(UInt64),
                [module.TypeSystem.Single] = sizeof(Single),
                [module.TypeSystem.Double] = sizeof(Double)
            };
            //sizes[UIntPtr] = sizeof(UIntPtr);
            
            //types[TypeType] = TypeType;
        }

        static TypeFactory()
        {
            
            ArrayCopyMethod = ArrayType.GetMethod("Copy", new Type[3] { ArrayType, ArrayType, Int32Type });
            ArrayLengthGetMethod = ArrayType.GetMethod("get_Length");
            StringNullOrEmptyMethod = StringType.GetMethod("IsNullOrEmpty");
            GCHandleAlloc = GCHandleType.GetMethod("Alloc",new Type[1]{ ObjectType });
            GCHandleAllocPinned = GCHandleType.GetMethod("Alloc", new Type[2] { ObjectType, typeof(GCHandleType) });
            OffsetToStringDataProperty = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetProperty("OffsetToStringData",BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance).GetGetMethod();
            StringLengthMethod = StringType.GetProperty("Length").GetGetMethod();
            
            GCHandleFreeMethod = GCHandleType.GetMethod("Free");
            GetTypeFromHandleMethod = TypeType.GetMethod("GetTypeFromHandle");
            StringCopyMethod = StringType.GetMethod("Copy");
            CharToString = CharType.GetMethod("ToString", BindingFlags.Static | BindingFlags.Public);
            MathMinMethod = typeof(Math).GetMethod("Min", new Type[] { Int32Type, Int32Type });
            MarshalAllocHGlobalMethod = MarshalType.GetMethod("AllocHGlobal", new Type[1] { Int32Type });
            MarshalFreeHGlobalMethod = MarshalType.GetMethod("FreeHGlobal", new Type[1] { typeof(IntPtr) });
            MarshalCopyMethod = MarshalType.GetMethod("Copy", new Type[4] { typeof(byte[]), Int32Type, IntPtrType, Int32Type });
            MarshalSizeOfMethod = MarshalType.GetMethod("SizeOf", new Type[] { TypeType });
            NullableHasValueGetMethod = typeof(Nullable<>).GetProperty("HasValue").GetGetMethod();
            NullableGetValueOrDefaultMethod = typeof(Nullable<>).GetMethod("GetValueOrDefault", Type.EmptyTypes);
            EnvironmentIs64BitProcessGetMethod = typeof(Environment).GetProperty("Is64BitProcess", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetGetMethod();
            ActivatorCreateInstanceMethod = typeof(Activator).GetMethod("CreateInstance", Type.EmptyTypes);
            IEnumerableGenericGetEnumeratorMethod = IEnumerableGenericType.GetMethod("GetEnumerator");
            IEnumerableGetEnumeratorMethod = IEnumerableType.GetMethod("GetEnumerator");
            IEnumeratorMoveNextMethod = IEnumeratorType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public);
            MonitorEnterMethod = MonitorType.GetMethod("Enter", new Type[1] { ObjectType });
            MonitorExitMethod = MonitorType.GetMethod("Exit", new Type[1] { ObjectType });
            ConvertToByteMethod = ConvertType.GetMethod("ToByte", new Type[1] { ObjectType });
            ConvertToSByteMethod = ConvertType.GetMethod("ToSByte", new Type[1] { ObjectType });
            ConvertToInt16Method = ConvertType.GetMethod("ToInt16", new Type[1] { ObjectType });
            ConvertToUInt16Method = ConvertType.GetMethod("ToUInt16", new Type[1] { ObjectType });
            ConvertToInt32Method = ConvertType.GetMethod("ToInt32", new Type[1] { ObjectType });
            ConvertToUInt32Method = ConvertType.GetMethod("ToUInt32", new Type[1] { ObjectType });
            ConvertToInt64Method = ConvertType.GetMethod("ToInt64", new Type[1] { ObjectType });
            ConvertToUInt64Method = ConvertType.GetMethod("ToUInt64", new Type[1] { ObjectType });
            ConvertToCharMethod = ConvertType.GetMethod("ToChar", new Type[1] { ObjectType });
            ConvertToBooleanMethod = ConvertType.GetMethod("ToBoolean", new Type[1] { ObjectType });
            ConvertToDoubleMethod = ConvertType.GetMethod("ToDouble", new Type[1] { ObjectType });
            ConvertToSingleMethod = ConvertType.GetMethod("ToSingle", new Type[1] { ObjectType });
            IDisposableDisposeMethod = IDisposableType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public);

            SecurityRulesAttributeSkipVerificationInFullTrustProperty = typeof(SecurityRulesAttribute).GetProperty("SkipVerificationInFullTrust");

            IndexOutOfRangeCtor = typeof(IndexOutOfRangeException).GetConstructor(Type.EmptyTypes);
            AttributeCtor = typeof(Attribute).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First(item=> item.GetParameters().Length==0);
            ParamArrayAttributeCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes);
            DebuggableAttributeCtor = typeof(System.Diagnostics.DebuggableAttribute).GetConstructor(new Type[] { BoolType, BoolType });
            AssemblyKeyFileAttributeCtor = typeof(AssemblyKeyFileAttribute).GetConstructor(new Type[] { StringType });
            AssemblyDelaySignAttributeCtor = typeof(AssemblyDelaySignAttribute).GetConstructor(new Type[] { BoolType });
            TargetFrameworkAttributeCtor = typeof(TargetFrameworkAttribute).GetConstructor(new Type[] { StringType });
            SecurityRulesAttributeCtor = typeof(SecurityRulesAttribute).GetConstructor(new Type[] { typeof(SecurityRuleSet) });
            STAThreadAttributeCtor = typeof(STAThreadAttribute).GetConstructor(Type.EmptyTypes);
            CompilationRelaxationsAttributeCtor = typeof(System.Runtime.CompilerServices.CompilationRelaxationsAttribute).GetConstructor(new Type[] { Int32Type });
            AssemblyTitleAttributeCtor = typeof(AssemblyTitleAttribute).GetConstructor(new Type[] { StringType });
            AssemblyDescriptionAttributeCtor = typeof(AssemblyDescriptionAttribute).GetConstructor(new Type[] { StringType });
        }

        public static bool IsStandType(Mono.Cecil.TypeReference t)
        {
            return types.Contains(t);
        }

        public static int GetPrimitiveTypeSize(Mono.Cecil.TypeReference PrimitiveType)
        {
            return sizes[PrimitiveType];
        }
    }

    class NETGeneratorTools
    {
        private static Mono.Cecil.ModuleDefinition module;

        public static void Init(Mono.Cecil.ModuleDefinition modul)
        {
            module = modul;
        }

        public static void PushStind(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference elem_type)
        {
            switch (elem_type.MetadataType)
            {
                case Mono.Cecil.MetadataType.Boolean:
                case Mono.Cecil.MetadataType.Byte:
                case Mono.Cecil.MetadataType.SByte:
                    il.Emit(OpCodes.Stind_I1);
                    break;
                case Mono.Cecil.MetadataType.Char:
                case Mono.Cecil.MetadataType.Int16:
                case Mono.Cecil.MetadataType.UInt16:
                    il.Emit(OpCodes.Stind_I2);
                    break;
                case Mono.Cecil.MetadataType.Int32:
                case Mono.Cecil.MetadataType.UInt32:
                    il.Emit(OpCodes.Stind_I4);
                    break;
                case Mono.Cecil.MetadataType.Int64:
                case Mono.Cecil.MetadataType.UInt64:
                    il.Emit(OpCodes.Stind_I8);
                    break;
                case Mono.Cecil.MetadataType.Single:
                    il.Emit(OpCodes.Stind_R4);
                    break;
                case Mono.Cecil.MetadataType.Double:
                    il.Emit(OpCodes.Stind_R8);
                    break;
                default:
                    if (IsPointer(elem_type))
                        il.Emit(OpCodes.Stind_I);
                    else if (elem_type.IsGenericParameter)
                        il.Emit(OpCodes.Stobj, elem_type);
                    else if (IsEnum(elem_type))
                        il.Emit(OpCodes.Stind_I4);
                    else
                        if (elem_type.IsValueType)
                            il.Emit(OpCodes.Stobj, elem_type);
                        else
                            il.Emit(OpCodes.Stind_Ref);
                    break;
            }
        }
        
        public static void PushStelem(Mono.Cecil.Cil.ILProcessor il,Mono.Cecil.TypeReference elem_type)
        {
            switch (elem_type.MetadataType)
            {
                case Mono.Cecil.MetadataType.Boolean:
                case Mono.Cecil.MetadataType.Byte:
                case Mono.Cecil.MetadataType.SByte:
                    il.Emit(OpCodes.Stelem_I1);
                    break;
                case Mono.Cecil.MetadataType.Char:
                case Mono.Cecil.MetadataType.Int16:
                case Mono.Cecil.MetadataType.UInt16:
                    il.Emit(OpCodes.Stelem_I2);
                    break;
                case Mono.Cecil.MetadataType.Int32:
                case Mono.Cecil.MetadataType.UInt32:
                    il.Emit(OpCodes.Stelem_I4);
                    break;
                case Mono.Cecil.MetadataType.Int64:
                case Mono.Cecil.MetadataType.UInt64:
                    il.Emit(OpCodes.Stelem_I8);
                    break;
                case Mono.Cecil.MetadataType.Single:
                    il.Emit(OpCodes.Stelem_R4);
                    break;
                case Mono.Cecil.MetadataType.Double:
                    il.Emit(OpCodes.Stelem_R8);
                    break;
                default:
                    if (IsPointer(elem_type))
                        il.Emit(OpCodes.Stelem_I);
                    else if (elem_type.IsGenericParameter)
                        il.Emit(OpCodes.Stelem_Any, elem_type);
                    else if (IsEnum(elem_type))
                        il.Emit(OpCodes.Stelem_I4);
                    else 
                        if (elem_type.IsValueType) 
                            il.Emit(OpCodes.Stobj, elem_type);
                        else 
                            il.Emit(OpCodes.Stelem_Ref);
                    break;
            }
        }

        public static void PushParameterDereference(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference elem_type)
        {
            switch (elem_type.MetadataType)
            {
                case Mono.Cecil.MetadataType.Boolean:
                case Mono.Cecil.MetadataType.Byte:
                    il.Emit(OpCodes.Ldind_U1);
                    break;
                case Mono.Cecil.MetadataType.SByte:
                    il.Emit(OpCodes.Ldind_I1);
                    break;
                case Mono.Cecil.MetadataType.Char:
                case Mono.Cecil.MetadataType.UInt16:
                    il.Emit(OpCodes.Ldind_U2);
                    break;
                case Mono.Cecil.MetadataType.Int16:
                    il.Emit(OpCodes.Ldind_I2);
                    break;
                case Mono.Cecil.MetadataType.UInt32:
                    il.Emit(OpCodes.Ldind_U4);
                    break;
                case Mono.Cecil.MetadataType.Int32:
                    il.Emit(OpCodes.Ldind_I4);
                    break;
                case Mono.Cecil.MetadataType.Int64:
                case Mono.Cecil.MetadataType.UInt64:
                    il.Emit(OpCodes.Ldind_I8);
                    break;
                case Mono.Cecil.MetadataType.Single:
                    il.Emit(OpCodes.Ldind_R4);
                    break;
                case Mono.Cecil.MetadataType.Double:
                    il.Emit(OpCodes.Ldind_R8);
                    break;
                default:
                    if (IsPointer(elem_type))
                        il.Emit(OpCodes.Ldind_I);
                    else
                        if (elem_type.IsValueType || elem_type.IsGenericParameter)
                            il.Emit(OpCodes.Ldobj, elem_type);
                        else
                            il.Emit(OpCodes.Ldind_Ref);
                    break;
            }
        }

        public static void PushLdelem(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference elem_type, bool ldobj)
        {
            switch (elem_type.MetadataType)
            {
                case Mono.Cecil.MetadataType.Boolean:
                case Mono.Cecil.MetadataType.Byte:
                    il.Emit(OpCodes.Ldelem_U1);
                    break;
                case Mono.Cecil.MetadataType.SByte:
                    il.Emit(OpCodes.Ldelem_I1);
                    break;
                case Mono.Cecil.MetadataType.Char:
                case Mono.Cecil.MetadataType.Int16:
                    il.Emit(OpCodes.Ldelem_I2);
                    break;
                case Mono.Cecil.MetadataType.UInt16:
                    il.Emit(OpCodes.Ldelem_U2);
                    break;
                case Mono.Cecil.MetadataType.Int32:
                    il.Emit(OpCodes.Ldelem_I4);
                    break;
                case Mono.Cecil.MetadataType.UInt32:
                    il.Emit(OpCodes.Ldelem_U4);
                    break;
                case Mono.Cecil.MetadataType.Int64:
                case Mono.Cecil.MetadataType.UInt64:
                    il.Emit(OpCodes.Ldelem_I8);
                    break;
                case Mono.Cecil.MetadataType.Single:
                    il.Emit(OpCodes.Ldelem_R4);
                    break;
                case Mono.Cecil.MetadataType.Double:
                    il.Emit(OpCodes.Ldelem_R8);
                    break;
                default:
                    if (IsPointer(elem_type))
                        il.Emit(OpCodes.Ldelem_I);
                    else if (elem_type.IsGenericParameter)
                        il.Emit(OpCodes.Ldelem_Any, elem_type);
                    else
                        if (elem_type.IsValueType)//если это структура
                        {
                            il.Emit(OpCodes.Ldelema, elem_type);//почему a?
                            // проверки нужно ли заменять тип возвр. знач. метода get_val массива на указатель
                            if (ldobj || !(elem_type.FullName != TypeFactory.VoidType.FullName && elem_type.IsValueType && !TypeFactory.IsStandType(elem_type)))
                                il.Emit(OpCodes.Ldobj, elem_type);
                        }
                        else il.Emit(OpCodes.Ldelem_Ref);
                    break;
            }           
        }
        public static void LdcIntConst(Mono.Cecil.Cil.ILProcessor il, int e)
        {
            switch (e)
            {
                case -1: il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                case 6: il.Emit(OpCodes.Ldc_I4_6); break;
                case 7: il.Emit(OpCodes.Ldc_I4_7); break;
                case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                default:
                    if (e < sbyte.MinValue || e > sbyte.MaxValue)
                        il.Emit(OpCodes.Ldc_I4, e);
                    else
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)e);
                    break;
                /*if (e > sbyte.MinValue && e < sbyte.MaxValue)  //DarkStar Changed
                    il.Emit(OpCodes.Ldc_I4_S,(sbyte)e);
                else if (e > Int32.MinValue && e < Int32.MaxValue)  
                    il.Emit(OpCodes.Ldc_I4, (int)e); break;		*/
            }
        }

        public static void PushLdc(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference elem_type, object value)
        {
            switch (elem_type.MetadataType)
            {
                case Mono.Cecil.MetadataType.Boolean:
                case Mono.Cecil.MetadataType.Byte:
                    //il.Emit(OpCodes.Ldc_I4_S, Convert.ToByte(value));
                    LdcIntConst(il, Convert.ToByte(value));
                    break;
                case Mono.Cecil.MetadataType.SByte:
                    LdcIntConst(il, Convert.ToSByte(value));
                    //il.Emit(OpCodes.Ldc_I4_S, Convert.ToSByte(value));
                    break;
                case Mono.Cecil.MetadataType.Char:
                    LdcIntConst(il, Convert.ToChar(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToChar(value));
                    break;
                case Mono.Cecil.MetadataType.Int16:
                    LdcIntConst(il, Convert.ToInt32(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToInt32(value));
                    break;
                case Mono.Cecil.MetadataType.UInt16:
                    LdcIntConst(il, Convert.ToUInt16(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToUInt16(value));
                    break;
                case Mono.Cecil.MetadataType.Int32:
                    LdcIntConst(il,Convert.ToInt32(value));
                    break;
                case Mono.Cecil.MetadataType.UInt32:
                    LdcIntConst(il, (Int32)Convert.ToUInt32(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToUInt32(value));
                    break;
                case Mono.Cecil.MetadataType.Int64:
                    il.Emit(OpCodes.Ldc_I8, Convert.ToInt64(value));
                    break;
                case Mono.Cecil.MetadataType.UInt64:
                    UInt64 UInt64 = Convert.ToUInt64(value);
                    if (UInt64 > Int64.MaxValue)
                    {
                        //Это будет медленно работать. Надо переделать.
                        //Надо разобраться как сссделано в C#, там все нормально
                        Int64 tmp = (Int64)(UInt64 - Int64.MaxValue - 1);
                        il.Emit(OpCodes.Ldc_I8, tmp);
                        il.Emit(OpCodes.Conv_U8);
                        il.Emit(OpCodes.Ldc_I8, Int64.MaxValue);
                        il.Emit(OpCodes.Conv_U8);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Add);
                    }
                    else
                        il.Emit(OpCodes.Ldc_I8, Convert.ToInt64(value));
                    break;
                case Mono.Cecil.MetadataType.Single:
                    il.Emit(OpCodes.Ldc_R4, (Single)value);
                    break;
                case Mono.Cecil.MetadataType.Double:
                    il.Emit(OpCodes.Ldc_R8, (Double)value);
                    break;
                case Mono.Cecil.MetadataType.String:
                    il.Emit(OpCodes.Ldstr, (string)value);
                    break;
                default:
                    if (IsEnum(elem_type))
                        //il.Emit(OpCodes.Ldc_I4, (Int32)value);
                        LdcIntConst(il, (Int32)value);
                    else
                        throw new Exception("Немогу положить PushLdc для " + value.GetType().ToString());
                    break;
            }
        }

        public static void PushCast(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp, Mono.Cecil.TypeReference from_value_type)
        {
            if (IsPointer(tp))
                return;
            //(ssyy) Вставил 15.05.08
            if (from_value_type != null)
            {
                il.Emit(OpCodes.Box, from_value_type);
            }
            if (tp.IsValueType || tp.IsGenericParameter)
                il.Emit(OpCodes.Unbox_Any, tp);
            else
                il.Emit(OpCodes.Castclass, tp);
        }
        
        public static Mono.Cecil.Cil.VariableDefinition CreateLocalAndLoad(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
            il.Body.Variables.Add(lb);
            il.Emit(OpCodes.Stloc, lb);
            if (tp.IsValueType)
                il.Emit(OpCodes.Ldloca, lb);
            else
                il.Emit(OpCodes.Ldloc, lb);
            return lb;
        }
        
        public static Mono.Cecil.Cil.VariableDefinition CreateLocal(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
            il.Body.Variables.Add(lb);
            il.Emit(OpCodes.Stloc, lb);
            return lb;
        }
        
        public static Mono.Cecil.Cil.VariableDefinition CreateLocalAndLdloca(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
            il.Body.Variables.Add(lb);
            il.Emit(OpCodes.Stloc, lb);
            il.Emit(OpCodes.Ldloca, lb);
            return lb;
        }

        public static void CreateBoundedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            Mono.Cecil.Cil.Instruction lbl = il.Create(OpCodes.Nop);
            if (fb.IsStatic)
                il.Emit(OpCodes.Ldsfld, fb);
            else
            {
                //il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fb);
            }
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, lbl);
            if (!fb.IsStatic)
                il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, ti.def_cnstr);
            if (fb.IsStatic)
                il.Emit(OpCodes.Stsfld, fb);
            else
                il.Emit(OpCodes.Stfld, fb);
            il.Append(lbl);
        }

        public static void CreateBoudedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, TypeInfo ti)
        {
            Mono.Cecil.Cil.Instruction lbl = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, lb);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, lbl);
            il.Emit(OpCodes.Newobj, ti.def_cnstr);
            il.Emit(OpCodes.Stloc, lb);
            il.Append(lbl);
        }

        public static bool IsBoundedArray(TypeInfo ti)
        {
            return ti.arr_fld != null;
        }

        public static void FixField(Mono.Cecil.MethodDefinition mb, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            Mono.Cecil.Cil.ILProcessor il = mb.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fb);
            if (fb.FieldType.FullName == module.TypeSystem.String.FullName)
            {
                il.Emit(OpCodes.Ldc_I4, (int)GCHandleType.Pinned);
                il.Emit(OpCodes.Call, module.ImportReference(TypeFactory.GCHandleAllocPinned));
            }
            else
            {
                il.Emit(OpCodes.Call, module.ImportReference(TypeFactory.GCHandleAlloc));
            }
            il.Emit(OpCodes.Pop);
        }

        public static void CloneField(Mono.Cecil.MethodDefinition clone_meth, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            Mono.Cecil.Cil.ILProcessor il = clone_meth.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloca_S, (byte)0);
            il.Emit(OpCodes.Ldarg_0);
            if (ti.clone_meth != null)
            {
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldflda, fb);
                else
                    il.Emit(OpCodes.Ldfld, fb);
                il.Emit(OpCodes.Call, ti.clone_meth);
            }
            else
            {
                il.Emit(OpCodes.Ldfld, fb);
            }
            il.Emit(OpCodes.Stfld, fb);
        }

        public static void AssignField(Mono.Cecil.MethodDefinition ass_meth, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            Mono.Cecil.Cil.ILProcessor il = ass_meth.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarga_S, (byte)1);
            if (ti.clone_meth != null)
            {
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldflda, fb);
                else
                    il.Emit(OpCodes.Ldfld, fb);
                il.Emit(OpCodes.Call, ti.clone_meth);
            }
            else
            {
                il.Emit(OpCodes.Ldfld, fb);
            }
            il.Emit(OpCodes.Stfld, fb);
        }

        public static void PushTypeOf(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            il.Emit(OpCodes.Ldtoken, tp);
            il.Emit(OpCodes.Call, module.ImportReference(TypeFactory.GetTypeFromHandleMethod));
        }
        
        public static bool IsPointer(Mono.Cecil.TypeReference tp)
        {
            return tp.IsPointer; /*|| tp==TypeFactory.IntPtr; INTPTR TODO*/
        }

        public static bool IsEnum(Mono.Cecil.TypeReference tp)
        {
            return !(tp is Mono.Cecil.TypeSpecification) && tp.Resolve().IsEnum;
        }
        
    }
}
