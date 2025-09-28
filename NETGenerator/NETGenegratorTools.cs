// Copyright (c) Ivan Bondarev, Stanislav Mikhalkovich (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)
using System;
using System.Collections.Generic;
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
        public static Mono.Cecil.TypeReference AttributeType = typeof(Attribute);
        public static Type DefaultMemberAttributeType = typeof(DefaultMemberAttribute);
        public static Type ConditionalAttributeType = typeof(System.Diagnostics.ConditionalAttribute);

        public static Mono.Cecil.TypeReference ExceptionType = typeof(Exception);
        public static Type VoidType = typeof(void);
        public static Mono.Cecil.TypeReference StringType = typeof(string);
        public static Mono.Cecil.TypeReference ObjectType = typeof(object);
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
        public static Mono.Cecil.TypeReference IEnumerableType = typeof(System.Collections.IEnumerable);
        public static Type IEnumeratorType = typeof(System.Collections.IEnumerator);
        public static Type IDisposableType = typeof(IDisposable);
        public static Mono.Cecil.TypeReference IEnumerableGenericType = typeof(System.Collections.Generic.IEnumerable<>);
        public static Type IEnumeratorGenericType = typeof(System.Collections.Generic.IEnumerator<>);

        private static HashSet<Type> types;
        private static Dictionary<Type, int> sizes;

        public static Mono.Cecil.MethodReference ArrayCopyMethod;
        public static Mono.Cecil.MethodReference ArrayLengthGetMethod;
        public static MethodInfo GetTypeFromHandleMethod;
        public static MethodInfo ResizeMethod;
        public static MethodInfo GCHandleFreeMethod;
        public static Mono.Cecil.MethodReference StringCopyMethod;
        public static Mono.Cecil.MethodReference StringNullOrEmptyMethod;
        public static Mono.Cecil.MethodReference GCHandleAlloc;
        public static Mono.Cecil.MethodReference GCHandleAllocPinned;
        public static Mono.Cecil.MethodReference OffsetToStringDataProperty;
        public static Mono.Cecil.MethodReference StringLengthMethod;
        public static Mono.Cecil.MethodReference CharToString;
        public static Mono.Cecil.MethodReference MathMinMethod;
        public static Mono.Cecil.MethodReference MarshalAllocHGlobalMethod;
        public static Mono.Cecil.MethodReference MarshalFreeHGlobalMethod;
        public static Mono.Cecil.MethodReference MarshalCopyMethod;
        public static Mono.Cecil.MethodReference MarshalSizeOfMethod;
        public static Mono.Cecil.MethodReference NullableHasValueGetMethod;
        public static Mono.Cecil.MethodReference NullableGetValueOrDefaultMethod;
        public static Mono.Cecil.MethodReference EnvironmentIs64BitProcessGetMethod;
        public static Mono.Cecil.MethodReference ActivatorCreateInstanceMethod;
        public static Mono.Cecil.MethodReference IEnumerableGenericGetEnumeratorMethod;
        public static Mono.Cecil.MethodReference IEnumerableGetEnumeratorMethod;
        public static Mono.Cecil.MethodReference IEnumeratorMoveNextMethod;
        public static Mono.Cecil.MethodReference MonitorEnterMethod;
        public static Mono.Cecil.MethodReference MonitorExitMethod;
        public static Mono.Cecil.MethodReference ConvertToByteMethod;
        public static Mono.Cecil.MethodReference ConvertToSByteMethod;
        public static Mono.Cecil.MethodReference ConvertToInt16Method;
        public static Mono.Cecil.MethodReference ConvertToUInt16Method;
        public static Mono.Cecil.MethodReference ConvertToInt32Method;
        public static Mono.Cecil.MethodReference ConvertToUInt32Method;
        public static Mono.Cecil.MethodReference ConvertToInt64Method;
        public static Mono.Cecil.MethodReference ConvertToUInt64Method;
        public static Mono.Cecil.MethodReference ConvertToCharMethod;
        public static Mono.Cecil.MethodReference ConvertToBooleanMethod;
        public static Mono.Cecil.MethodReference ConvertToDoubleMethod;
        public static Mono.Cecil.MethodReference ConvertToSingleMethod;
        public static Mono.Cecil.MethodReference IDisposableDisposeMethod;

        public static Mono.Cecil.PropertyReference SecurityRulesAttributeSkipVerificationInFullTrustProperty;

        public static Mono.Cecil.MethodReference IndexOutOfRangeCtor;
        public static Mono.Cecil.MethodReference ParamArrayAttributeCtor;
        public static Mono.Cecil.MethodReference DebuggableAttributeCtor;
        public static Mono.Cecil.MethodReference AssemblyKeyFileAttributeCtor;
        public static Mono.Cecil.MethodReference AssemblyDelaySignAttributeCtor;
        public static Mono.Cecil.MethodReference TargetFrameworkAttributeCtor;
        public static Mono.Cecil.MethodReference SecurityRulesAttributeCtor;
        public static Mono.Cecil.MethodReference STAThreadAttributeCtor;
        public static Mono.Cecil.MethodReference CompilationRelaxationsAttributeCtor;
        public static Mono.Cecil.MethodReference AssemblyTitleAttributeCtor;
        public static Mono.Cecil.MethodReference AssemblyDescriptionAttributeCtor;

        static TypeFactory()
        {
            types = new HashSet<Type>()
            {
                BoolType, SByteType, ByteType, CharType,
                Int16Type, Int32Type, Int64Type,
                UInt16Type, UInt32Type, UInt64Type,
                SingleType, DoubleType
            };

            sizes = new Dictionary<Type, int>
            {
                [BoolType] = sizeof(Boolean),
                [SByteType] = sizeof(SByte),
                [ByteType] = sizeof(Byte),
                [CharType] = sizeof(Char),
                [Int16Type] = sizeof(Int16),
                [Int32Type] = sizeof(Int32),
                [Int64Type] = sizeof(Int64),
                [UInt16Type] = sizeof(UInt16),
                [UInt32Type] = sizeof(UInt32),
                [UInt64Type] = sizeof(UInt64),
                [SingleType] = sizeof(Single),
                [DoubleType] = sizeof(Double)
            };
            //sizes[UIntPtr] = sizeof(UIntPtr);
            
            //types[TypeType] = TypeType;
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
        public static void PushStind(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference elem_type)
        {
            switch (Type.GetTypeCode(elem_type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                    il.Emit(OpCodes.Stind_I1);
                    break;
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    il.Emit(OpCodes.Stind_I2);
                    break;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    il.Emit(OpCodes.Stind_I4);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    il.Emit(OpCodes.Stind_I8);
                    break;
                case TypeCode.Single:
                    il.Emit(OpCodes.Stind_R4);
                    break;
                case TypeCode.Double:
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
            switch (Type.GetTypeCode(elem_type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                    il.Emit(OpCodes.Stelem_I1);
                    break;
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    il.Emit(OpCodes.Stelem_I2);
                    break;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    il.Emit(OpCodes.Stelem_I4);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    il.Emit(OpCodes.Stelem_I8);
                    break;
                case TypeCode.Single:
                    il.Emit(OpCodes.Stelem_R4);
                    break;
                case TypeCode.Double:
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
            switch (Type.GetTypeCode(elem_type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                    il.Emit(OpCodes.Ldind_U1);
                    break;
                case TypeCode.SByte:
                    il.Emit(OpCodes.Ldind_I1);
                    break;
                case TypeCode.Char:
                case TypeCode.UInt16:
                    il.Emit(OpCodes.Ldind_U2);
                    break;
                case TypeCode.Int16:
                    il.Emit(OpCodes.Ldind_I2);
                    break;
                case TypeCode.UInt32:
                    il.Emit(OpCodes.Ldind_U4);
                    break;
                case TypeCode.Int32:
                    il.Emit(OpCodes.Ldind_I4);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    il.Emit(OpCodes.Ldind_I8);
                    break;
                case TypeCode.Single:
                    il.Emit(OpCodes.Ldind_R4);
                    break;
                case TypeCode.Double:
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
            switch (Type.GetTypeCode(elem_type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                    il.Emit(OpCodes.Ldelem_U1);
                    break;
                case TypeCode.SByte:
                    il.Emit(OpCodes.Ldelem_I1);
                    break;
                case TypeCode.Char:
                case TypeCode.Int16:
                    il.Emit(OpCodes.Ldelem_I2);
                    break;
                case TypeCode.UInt16:
                    il.Emit(OpCodes.Ldelem_U2);
                    break;
                case TypeCode.Int32:
                    il.Emit(OpCodes.Ldelem_I4);
                    break;
                case TypeCode.UInt32:
                    il.Emit(OpCodes.Ldelem_U4);
                    break;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    il.Emit(OpCodes.Ldelem_I8);
                    break;
                case TypeCode.Single:
                    il.Emit(OpCodes.Ldelem_R4);
                    break;
                case TypeCode.Double:
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
                            if (ldobj || !(elem_type != TypeFactory.VoidType && elem_type.IsValueType && !TypeFactory.IsStandType(elem_type)))
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
            switch (Type.GetTypeCode(elem_type))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                    //il.Emit(OpCodes.Ldc_I4_S, Convert.ToByte(value));
                    LdcIntConst(il, Convert.ToByte(value));
                    break;
                case TypeCode.SByte:
                    LdcIntConst(il, Convert.ToSByte(value));
                    //il.Emit(OpCodes.Ldc_I4_S, Convert.ToSByte(value));
                    break;
                case TypeCode.Char:
                    LdcIntConst(il, Convert.ToChar(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToChar(value));
                    break;
                case TypeCode.Int16:
                    LdcIntConst(il, Convert.ToInt32(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToInt32(value));
                    break;
                case TypeCode.UInt16:
                    LdcIntConst(il, Convert.ToUInt16(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToUInt16(value));
                    break;
                case TypeCode.Int32:
                    LdcIntConst(il,Convert.ToInt32(value));
                    break;
                case TypeCode.UInt32:
                    LdcIntConst(il, (Int32)Convert.ToUInt32(value));
                    //il.Emit(OpCodes.Ldc_I4, Convert.ToUInt32(value));
                    break;
                case TypeCode.Int64:
                    il.Emit(OpCodes.Ldc_I8, Convert.ToInt64(value));
                    break;
                case TypeCode.UInt64:
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
                case TypeCode.Single:
                    il.Emit(OpCodes.Ldc_R4, (Single)value);
                    break;
                case TypeCode.Double:
                    il.Emit(OpCodes.Ldc_R8, (Double)value);
                    break;
                case TypeCode.String:
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
        
        public static LocalBuilder CreateLocalAndLoad(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            LocalBuilder lb = il.DeclareLocal(tp);
            il.Emit(OpCodes.Stloc, lb);
            if (tp.IsValueType)
                il.Emit(OpCodes.Ldloca, lb);
            else
                il.Emit(OpCodes.Ldloc, lb);
            return lb;
        }
        
        public static Mono.Cecil.Cil.VariableDefinition CreateLocal(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            LocalBuilder lb = il.DeclareLocal(tp);
            il.Emit(OpCodes.Stloc, lb);
            return lb;
        }
        
        public static LocalBuilder CreateLocalAndLdloca(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.TypeReference tp)
        {
            LocalBuilder lb = il.DeclareLocal(tp);
            il.Emit(OpCodes.Stloc, lb);
            il.Emit(OpCodes.Ldloca, lb);
            return lb;
        }

        public static void CreateBoundedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            Label lbl = il.DefineLabel();
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
            il.MarkLabel(lbl);
        }

        public static void CreateBoudedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, TypeInfo ti)
        {
            Label lbl = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lb);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, lbl);
            il.Emit(OpCodes.Newobj, ti.def_cnstr);
            il.Emit(OpCodes.Stloc, lb);
            il.MarkLabel(lbl);
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
            if (fb.FieldType == TypeFactory.StringType)
            {
                il.Emit(OpCodes.Ldc_I4, (int)GCHandleType.Pinned);
                il.Emit(OpCodes.Call, TypeFactory.GCHandleAllocPinned);
            }
            else
            {
                il.Emit(OpCodes.Call, TypeFactory.GCHandleAlloc);
            }
            il.Emit(OpCodes.Pop);
        }

        public static void CloneField(Mono.Cecil.MethodDefinition clone_meth, Mono.Cecil.FieldDefinition fb, TypeInfo ti)
        {
            ILGenerator il = clone_meth.GetILGenerator();
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
            ILGenerator il = ass_meth.GetILGenerator();
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
            il.Emit(OpCodes.Call, TypeFactory.GetTypeFromHandleMethod);
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
