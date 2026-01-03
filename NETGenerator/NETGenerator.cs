// Copyright (c) Ivan Bondarev, Stanislav Mikhalkovich (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Threading;
using Mono.Cecil.Rocks;
using PascalABCCompiler.NetHelper;
using PascalABCCompiler.SemanticTree;

// TODO это нужно удалить
using OpCodes = Mono.Cecil.Cil.OpCodes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using EventAttributes = Mono.Cecil.EventAttributes;
using PInvokeAttributes = Mono.Cecil.PInvokeAttributes;
using Instruction = Mono.Cecil.Cil.Instruction;

namespace PascalABCCompiler.NETGenerator
{

    public enum TargetType
    {
        Exe,
        Dll,
        WinExe
    }

    public enum DebugAttributes
    {
        Debug,
        ForDebugging,
        Release
    }


    //compiler options class
    public class CompilerOptions
    {
        public enum PlatformTarget { x64, x86, AnyCPU, dotnet5win, dotnet5linux, dotnet5macos, dotnetwinnative, dotnetlinuxnative, dotnetmacosnative };

        public TargetType target = TargetType.Exe;
        public DebugAttributes dbg_attrs = DebugAttributes.Release;
        public bool optimize = false;
        public bool ForRunningWithEnvironment = false;
        public bool GenerateDebugInfoForInitalValue = true;

        public bool NeedDefineVersionInfo = false;
        private string _Product = "";
        private PlatformTarget _platformtarget = PlatformTarget.AnyCPU;
        public Type RtlPABCSystemType;
        
        public PlatformTarget platformtarget
        {
            get { return _platformtarget; }
            set { _platformtarget = value; }
        }

        private string _TargetFramework = "";

        public string TargetFramework
        {
            get { return _TargetFramework; }
            set { _TargetFramework = value; }
        }

        public string Product
        {
            get { return _Product; }
            set { _Product = value; NeedDefineVersionInfo = true; }
        }
        private string _ProductVersion = "";

        public string ProductVersion
        {
            get { return _ProductVersion; }
            set { _ProductVersion = value; NeedDefineVersionInfo = true; }
        }
        private string _Company = "";

        public string Company
        {
            get { return _Company; }
            set { _Company = value; NeedDefineVersionInfo = true; }
        }
        private string _Copyright = "";

        public string Copyright
        {
            get { return _Copyright; }
            set { _Copyright = value; NeedDefineVersionInfo = true; }
        }
        private string _TradeMark = "";

        public string TradeMark
        {
            get { return _TradeMark; }
            set { _TradeMark = value; NeedDefineVersionInfo = true; }
        }
        private string _Title = "";

        public string Title
        {
            get { return _Title; }
            set { _Title = value; NeedDefineVersionInfo = true; }
        }
        private string _Description = "";

        public string Description
        {
            get { return _Description; }
            set { _Description = value; NeedDefineVersionInfo = true; }
        }

        public string MainResourceFileName = null;

        public byte[] MainResourceData = null;
        public CompilerOptions() { }
    }

    /// <summary>
    /// Класс, переводящий сем. дерево в сборку .NET
    /// </summary>
    public class ILConverter : AbstractVisitor
    {
        protected AppDomain ad;//домен приложения (в нем будет генерироваться сборка)
        protected Mono.Cecil.AssemblyNameDefinition an;//имя сборки
        protected Mono.Cecil.AssemblyDefinition ab;//билдер для сборки
        protected Mono.Cecil.ModuleDefinition mb;//билдер для модуля
        protected Mono.Cecil.TypeDefinition entry_type;//тип-обертка над осн. программой
        protected Mono.Cecil.TypeDefinition cur_type;//текущий компилируемый тип
        protected Mono.Cecil.MethodDefinition entry_meth;//входная точка в приложение
        protected Mono.Cecil.MethodDefinition cur_meth;//текущий билдер для метода
        protected Mono.Cecil.MethodDefinition init_variables_mb;
        protected Mono.Cecil.Cil.ILProcessor il;//стандартный класс для генерации IL-кода
        protected Mono.Cecil.Cil.Document doc;//класс для генерации отладочной информации
        protected Mono.Cecil.Cil.Document first_doc;//класс для генерации отладочной информации
        protected Stack<Instruction> labels = new Stack<Instruction>();//стек меток для break
        protected Stack<Instruction> clabels = new Stack<Instruction>();//стек меток для continue
        protected Stack<MethInfo> smi = new Stack<MethInfo>();//стек вложенных функций
        protected Helper helper; //привязывает классы сем. дерева к нетовским билдерам
        protected int num_scope = 0;//уровень вложенности
        protected List<Mono.Cecil.TypeDefinition> types = new List<Mono.Cecil.TypeDefinition>();//список закрытия типов
        protected List<Mono.Cecil.TypeDefinition> value_types = new List<Mono.Cecil.TypeDefinition>();//список закрытия размерных типов (треб. особый порядок)
        protected int uid = 1;//счетчик для задания уникальных имен (исп. при именовании классов-оболочек над влож. ф-ми)
        protected List<ICommonFunctionNode> funcs = new List<ICommonFunctionNode>();//
        protected bool is_addr = false;//флаг, передается ли значение как факт. var-параметр
        protected bool copy_string = false;
        protected string cur_unit;//имя текущего модуля
        protected Mono.Cecil.MethodDefinition cur_cnstr;//текущий конструктор - тоже нужен (ssyy)
        protected bool is_dot_expr = false;//флаг, стоит ли после выражения точка (нужно для упаковки размерных типов)
        protected bool is_field_reference = false;
        protected TypeInfo cur_ti;//текущий клас
        protected CompilerOptions comp_opt = new CompilerOptions();//опции компилятора
        protected Dictionary<string, Mono.Cecil.Cil.Document> sym_docs = new Dictionary<string, Mono.Cecil.Cil.Document>();//таблица отладочных документов
        protected bool is_constructor = false;//флаг, переводим ли мы конструктор
        protected bool init_call_awaited = false;
        protected bool save_debug_info = false;
        protected ILocation next_location;
        protected bool add_special_debug_variables = false;
        protected bool make_next_spoint = true;
        protected SemanticTree.ILocation EntryPointLocation;
        protected Instruction ExitLabel;//метка для выхода из процедуры
        protected bool ExitProcedureCall = false; //признак того, что встретилась exit и надо пометить конец процедуры
        protected Dictionary<IConstantNode, Mono.Cecil.FieldDefinition> ConvertedConstants = new Dictionary<IConstantNode, Mono.Cecil.FieldDefinition>();
        //ivan
        protected List<Mono.Cecil.TypeDefinition> enums = new List<Mono.Cecil.TypeDefinition>();
        protected List<Mono.Cecil.TypeDefinition> NamespaceTypesList = new List<Mono.Cecil.TypeDefinition>();
        protected Mono.Cecil.TypeDefinition cur_unit_type;
        private Dictionary<IFunctionNode, IFunctionNode> prop_accessors = new Dictionary<IFunctionNode, IFunctionNode>();
        //ssyy
        private const int num_try_save = 10; //Кол-во попыток сохранения
        private ICommonTypeNode converting_generic_param = null;
        private Dictionary<ICommonFunctionNode, List<IGenericTypeInstance>> instances_in_functions =
            new Dictionary<ICommonFunctionNode, List<IGenericTypeInstance>>();

        //\ssyy
        
        private Mono.Cecil.MethodReference fix_pointer_meth = null;
        private Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.TypeDefinition> marked_with_extension_attribute = new Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.TypeDefinition>();

        private Mono.Cecil.Cil.VariableDefinition current_index_lb;
        private bool has_dereferences = false;
        private bool safe_block = false;
        private int cur_line = 0;
        private Mono.Cecil.Cil.Document new_doc;
        private bool pabc_rtl_converted = false;
        bool has_unmanaged_resources = false;

        private void CheckLocation(SemanticTree.ILocation Location)
        {
            if (Location != null)
            {
                Mono.Cecil.Cil.Document temp_doc = null;
                if (sym_docs.ContainsKey(Location.file_name))
                {
                    temp_doc = sym_docs[Location.file_name];
                }
                else
                if (save_debug_info) // иногда вызывается MarkSequencePoint при save_debug_info = false
                {
                    temp_doc = new Mono.Cecil.Cil.Document(Location.file_name)
                    {
                        Type = Mono.Cecil.Cil.DocumentType.Text,
                        Language = Mono.Cecil.Cil.DocumentLanguage.Pascal,
                        LanguageVendor = Mono.Cecil.Cil.DocumentLanguageVendor.Microsoft
                    };

                    sym_docs.Add(Location.file_name, temp_doc);
                }
                if (temp_doc != doc)
                {
                    doc = temp_doc;
                    cur_line = -1;
                }
            }
        }

        private bool OnNextLine(ILocation loc)
        {
            if (doc != new_doc)
            {
                new_doc = doc;
                cur_line = loc.begin_line_num;
                return true;
            }
            if (loc.begin_line_num == cur_line) return false;
            cur_line = loc.begin_line_num;
            return true;
        }

        public bool EnterSafeBlock()
        {
            bool tmp = safe_block;
            safe_block = true;
            return tmp;
        }

        public void LeaveSafeBlock(bool value)
        {
            safe_block = value;
        }

        protected void MarkSequencePoint(SemanticTree.ILocation Location)
        {
            CheckLocation(Location);
            if (Location != null && OnNextLine(Location))
                MarkSequencePoint(il, Location);
        }

        protected void MarkSequencePointToEntryPoint(Mono.Cecil.Cil.ILProcessor ilg)
        {
            MarkSequencePoint(ilg, EntryPointLocation);
        }

        protected void MarkSequencePoint(Mono.Cecil.Cil.ILProcessor ilg, SemanticTree.ILocation Location)
        {
            if (Location != null)
            {
                CheckLocation(Location);
                MarkSequencePoint(ilg, Location.begin_line_num, Location.begin_column_num, Location.end_line_num, Location.end_column_num);
            }
        }

        protected void MarkSequencePoint(Mono.Cecil.Cil.ILProcessor ilg, int bl, int bc, int el, int ec)
        {
            var body = ilg.Body;
            var instr = body.Instructions.Last();

            var seqPoint = new Mono.Cecil.Cil.SequencePoint(instr, doc)
            {
                StartLine = bl, StartColumn = bc,
                EndLine = el, EndColumn = ec + 1
            };

            //if (make_next_spoint)
            body.Method.DebugInformation.SequencePoints.Add(seqPoint);
            make_next_spoint = true;
        }

        private Dictionary<string, string> StandartDirectories;

        public ILConverter(Dictionary<string, string> StandartDirectories)
        {
            this.StandartDirectories = StandartDirectories;
        }

        int TempNamesCount = 0;
        private string GetTempName()
        {
            return String.Format("$PABCNET_TN{0}$", TempNamesCount++);
        }

        protected Mono.Cecil.FieldDefinition GetConvertedConstants(IConstantNode c)
        {
            if (ConvertedConstants.ContainsKey(c))
                return ConvertedConstants[c];
            Mono.Cecil.Cil.ILProcessor ilb = il;
            if (entry_type != null && false)
                il = ModulesInitILGenerators[entry_type];
            else
                il = ModulesInitILGenerators[cur_unit_type];
            ConvertConstantDefinitionNode(null, GetTempName(), c.type, c);
            il = ilb;
            il.Emit(OpCodes.Call, helper.GetDummyMethod(cur_unit_type));
            return ConvertedConstants[c];
        }

        private Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.Cil.ILProcessor> ModulesInitILGenerators = new Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.Cil.ILProcessor>();

        private Mono.Cecil.MethodDefinition fileOfAttributeConstructor;

        private Mono.Cecil.MethodDefinition FileOfAttributeConstructor
        {
            get
            {
                if (fileOfAttributeConstructor != null) return fileOfAttributeConstructor;
                
                var dotIndex = StringConstants.file_of_attr_name.IndexOf('.');
                var ns = StringConstants.file_of_attr_name.Substring(0, dotIndex);
                var className = StringConstants.file_of_attr_name.Substring(dotIndex + 1);

                var tb = new Mono.Cecil.TypeDefinition(ns, className, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                mb.Types.Add(tb);
                types.Add(tb);

                fileOfAttributeConstructor = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                fileOfAttributeConstructor.HasThis = true;
                fileOfAttributeConstructor.Parameters.Add
                (
                    new Mono.Cecil.ParameterDefinition(mb.TypeSystem.Object)
                );

                tb.Methods.Add(fileOfAttributeConstructor);

                var fld = new Mono.Cecil.FieldDefinition("Type", FieldAttributes.Public, mb.TypeSystem.Object);
                tb.Fields.Add(fld);

                var cnstr_il = fileOfAttributeConstructor.Body.GetILProcessor();
                cnstr_il.Emit(OpCodes.Ldarg_0);
                cnstr_il.Emit(OpCodes.Ldarg_1);
                cnstr_il.Emit(OpCodes.Stfld, fld);
                cnstr_il.Emit(OpCodes.Ret);

                return fileOfAttributeConstructor;
            }
        }

        private Mono.Cecil.MethodDefinition setOfAttributeConstructor;

        private Mono.Cecil.MethodDefinition SetOfAttributeConstructor
        {
            get
            {
                if (setOfAttributeConstructor != null) return setOfAttributeConstructor;


                var dotIndex = StringConstants.set_of_attr_name.IndexOf('.');
                var ns = StringConstants.set_of_attr_name.Substring(0, dotIndex);
                var className = StringConstants.set_of_attr_name.Substring(dotIndex + 1);

                var tb = new Mono.Cecil.TypeDefinition(ns, className, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                mb.Types.Add(tb);
                types.Add(tb);

                setOfAttributeConstructor = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public, mb.TypeSystem.Void);
                setOfAttributeConstructor.HasThis = true;
                setOfAttributeConstructor.Parameters.Add
                (
                    new Mono.Cecil.ParameterDefinition(mb.TypeSystem.Object)
                );

                tb.Methods.Add(setOfAttributeConstructor);

                var fld = new Mono.Cecil.FieldDefinition("Type", FieldAttributes.Public, mb.TypeSystem.Object);
                tb.Fields.Add(fld);

                var cnstr_il = setOfAttributeConstructor.Body.GetILProcessor();
                cnstr_il.Emit(OpCodes.Ldarg_0);
                cnstr_il.Emit(OpCodes.Ldarg_1);
                cnstr_il.Emit(OpCodes.Stfld, fld);
                cnstr_il.Emit(OpCodes.Ret);

                return setOfAttributeConstructor;
            }
        }

        private Mono.Cecil.MethodDefinition templateClassAttributeConstructor;

        private Mono.Cecil.MethodDefinition TemplateClassAttributeConstructor
        {
            get
            {
                if (templateClassAttributeConstructor != null) return templateClassAttributeConstructor;


                var dotIndex = StringConstants.template_class_attr_name.IndexOf('.');
                var ns = StringConstants.template_class_attr_name.Substring(0, dotIndex);
                var className = StringConstants.template_class_attr_name.Substring(dotIndex + 1);

                var tb = new Mono.Cecil.TypeDefinition(ns, className, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                mb.Types.Add(tb);
                types.Add(tb);

				templateClassAttributeConstructor = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public, mb.TypeSystem.Void);
                templateClassAttributeConstructor.HasThis = true;
                templateClassAttributeConstructor.Parameters.Add
                (
                    new Mono.Cecil.ParameterDefinition( mb.TypeSystem.Byte.MakeArrayType() )
                );

                tb.Methods.Add(templateClassAttributeConstructor);

                var fld = new Mono.Cecil.FieldDefinition("Tree", FieldAttributes.Public, mb.TypeSystem.Byte.MakeArrayType());
                tb.Fields.Add(fld);

                var cnstr_il = templateClassAttributeConstructor.Body.GetILProcessor();
                cnstr_il.Emit(OpCodes.Ldarg_0);
                cnstr_il.Emit(OpCodes.Ldarg_1);
                cnstr_il.Emit(OpCodes.Stfld, fld);
                cnstr_il.Emit(OpCodes.Ret);

                return templateClassAttributeConstructor;
            }
        }

        private Mono.Cecil.MethodDefinition typeSynonimAttributeConstructor;

        private Mono.Cecil.MethodDefinition TypeSynonimAttributeConstructor
        {
            get
            {
                if (typeSynonimAttributeConstructor != null) return typeSynonimAttributeConstructor;

                var dotIndex = StringConstants.type_synonim_attr_name.IndexOf('.');
                var ns = StringConstants.type_synonim_attr_name.Substring(0, dotIndex);
                var className = StringConstants.type_synonim_attr_name.Substring(dotIndex + 1);

                var tb = new Mono.Cecil.TypeDefinition(ns, className, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                mb.Types.Add(tb);
                types.Add(tb);

                typeSynonimAttributeConstructor = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public, mb.TypeSystem.Void);
                typeSynonimAttributeConstructor.HasThis = true;
				typeSynonimAttributeConstructor.Parameters.Add
                (
                    new Mono.Cecil.ParameterDefinition(mb.TypeSystem.Object)
                );

                tb.Methods.Add(typeSynonimAttributeConstructor);

                var fld = new Mono.Cecil.FieldDefinition("Type", FieldAttributes.Public, mb.TypeSystem.Object);
                tb.Fields.Add(fld);

                var cnstr_il = typeSynonimAttributeConstructor.Body.GetILProcessor();
                cnstr_il.Emit(OpCodes.Ldarg_0);
                cnstr_il.Emit(OpCodes.Ldarg_1);
                cnstr_il.Emit(OpCodes.Stfld, fld);
                cnstr_il.Emit(OpCodes.Ret);

                return typeSynonimAttributeConstructor;
            }
        }

        private Mono.Cecil.MethodDefinition shortStringAttributeConstructor;

        private Mono.Cecil.MethodDefinition ShortStringAttributeConstructor
        {
            get
            {
                if (shortStringAttributeConstructor != null) return shortStringAttributeConstructor;

                var dotIndex = StringConstants.short_string_attr_name.IndexOf('.');
                var ns = StringConstants.short_string_attr_name.Substring(0, dotIndex);
                var className = StringConstants.short_string_attr_name.Substring(dotIndex + 1);

                var tb = new Mono.Cecil.TypeDefinition(ns, className, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                mb.Types.Add(tb);
                types.Add(tb);

				shortStringAttributeConstructor = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public, mb.TypeSystem.Void);
                shortStringAttributeConstructor.HasThis = true;
				shortStringAttributeConstructor.Parameters.Add
                (
                    new Mono.Cecil.ParameterDefinition(mb.TypeSystem.Int32)
                );

                tb.Methods.Add(shortStringAttributeConstructor);

                var fld = new Mono.Cecil.FieldDefinition("Length", FieldAttributes.Public, mb.TypeSystem.Int32);
                tb.Fields.Add(fld);

                var cnstr_il = shortStringAttributeConstructor.Body.GetILProcessor();
                cnstr_il.Emit(OpCodes.Ldarg_0);
                cnstr_il.Emit(OpCodes.Ldarg_1);
                cnstr_il.Emit(OpCodes.Stfld, fld);
                cnstr_il.Emit(OpCodes.Ret);

                return shortStringAttributeConstructor;
            }
        }

        private ICommonFunctionNode GetGenericFunctionContainer(ITypeNode tn)
        {
            if (tn.common_generic_function_container != null)
            {
                return tn.common_generic_function_container;
            }
            if (tn.type_special_kind == type_special_kind.typed_file)
            {
                return GetGenericFunctionContainer(tn.element_type);
            }
            if (tn.type_special_kind == type_special_kind.set_type)
            {
                return GetGenericFunctionContainer(tn.element_type);
            }
            if (tn.type_special_kind == type_special_kind.array_kind)
            {
                return GetGenericFunctionContainer(tn.element_type);
            }
            IRefTypeNode ir = tn as IRefTypeNode;
            if (ir != null)
            {
                return GetGenericFunctionContainer(ir.pointed_type);
            }
            IGenericTypeInstance igti = tn as IGenericTypeInstance;
            if (igti != null)
            {
                foreach (ITypeNode par in igti.generic_parameters)
                {
                    ICommonFunctionNode rez = GetGenericFunctionContainer(par);
                    if (rez != null)
                    {
                        return rez;
                    }
                }
            }
            return null;
        }

        private void AddTypeInstanceToFunction(ICommonFunctionNode func, IGenericTypeInstance gti)
        {
            if (func == null)
                return;
            List<IGenericTypeInstance> instances;
            //if (func == null) // SSM 3.07.16 Это решает проблему с оставшимся после перевода в сем. дерево узлом IEnumerable<UnknownType>, но очень грубо - пробую найти ошибку раньше
            //    return;
            bool found = instances_in_functions.TryGetValue(func, out instances);
            if (!found)
            {
                instances = new List<IGenericTypeInstance>();
                instances_in_functions.Add(func, instances);
            }
            if (!instances.Contains(gti))
            {
                instances.Add(gti);
            }
        }

        bool IsDllAndSystemNamespace(string name, string DllFileName)
        {
            return comp_opt.target == TargetType.Dll && DllFileName != "PABCRtl.dll" &&
                (name == StringConstants.pascalSystemUnitName || name == StringConstants.pascalExtensionsUnitName ||
                 name.EndsWith(StringConstants.ImplementationSectionNamespaceName));
        }

        bool IsDotnet5()
        {
            return 
                comp_opt.platformtarget == 
                  CompilerOptions.PlatformTarget.dotnet5win || 
                comp_opt.platformtarget == 
                  CompilerOptions.PlatformTarget.dotnet5linux || 
                comp_opt.platformtarget == 
                  CompilerOptions.PlatformTarget.dotnet5macos; // PVS 01/2022
        }

        bool IsDotnetNative()
        {
            return
                comp_opt.platformtarget ==
                  CompilerOptions.PlatformTarget.dotnetwinnative ||
                comp_opt.platformtarget ==
                  CompilerOptions.PlatformTarget.dotnetlinuxnative ||
                comp_opt.platformtarget ==
                  CompilerOptions.PlatformTarget.dotnetmacosnative; // PVS 01/2022
        }

        private void BuildDotnet5(string orig_dir, string dir, string publish_dir)
        {
            if (Directory.Exists(publish_dir))
                Directory.Delete(publish_dir, true);
            Directory.CreateDirectory(publish_dir);
            StringBuilder sb = new StringBuilder();
            string framework = "net5.0";
            if (comp_opt.target == TargetType.WinExe)
            {
                framework = "net5.0-windows";
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\">");
                sb.AppendLine("<PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>"+framework+ "</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup>");
                sb.AppendLine("<ItemGroup><Reference Include = \"" + an.Name + "\"><HintPath>" + Path.Combine(dir, an.Name) + ".dll" + "</HintPath></Reference></ItemGroup>");
                sb.AppendLine("</Project>");
            }
            else
            {
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
                sb.AppendLine("<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>"+framework+ "</TargetFramework></PropertyGroup>");
                sb.AppendLine("<ItemGroup><Reference Include = \"" + an.Name + "\"><HintPath>" + Path.Combine(dir, an.Name) + ".dll" + "</HintPath></Reference></ItemGroup>");
                sb.AppendLine("</Project>");
            }
           
            string csproj = Path.Combine(dir, Path.GetFileNameWithoutExtension(an.Name) + ".csproj");
            File.WriteAllText(csproj, sb.ToString());
            sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("namespace StartApp");
            sb.AppendLine("{");
            sb.AppendLine("class StartProgram");
            sb.AppendLine("{");
            sb.AppendLine("static void Main(string[] args)");
            sb.AppendLine("{");
            sb.AppendLine(entry_meth.DeclaringType.FullName+"."+entry_meth.Name+"();");
            sb.AppendLine("}");
            sb.AppendLine("}");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "Program.cs"), sb.ToString());
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo = new System.Diagnostics.ProcessStartInfo();
            p.StartInfo.FileName = "dotnet";
            string runtime = "win-x64";
            if (comp_opt.platformtarget == CompilerOptions.PlatformTarget.dotnet5linux)
                runtime = "linux-x64";
            else if (comp_opt.platformtarget == CompilerOptions.PlatformTarget.dotnet5macos)
                runtime = "osx.10.11-x64";
            string conf = "Debug";
            if (comp_opt.dbg_attrs == DebugAttributes.Release)
                conf = "Release";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = "publish -f "+framework+" --runtime "+runtime+" -c "+conf+ " --self-contained false " + csproj;
            p.Start();
            p.WaitForExit();
            var files = Directory.GetFiles(Path.Combine(dir, "bin" + Path.DirectorySeparatorChar + conf + Path.DirectorySeparatorChar + framework + Path.DirectorySeparatorChar + runtime + Path.DirectorySeparatorChar + "publish"));
            foreach (var file in files)
                File.Copy(file, Path.Combine(publish_dir, Path.GetFileName(file)));
            foreach (var file in files)
                File.Copy(file, Path.Combine(orig_dir, Path.GetFileName(file)), true);
        }

        private void BuildDotnetNative(SemanticTree.IProgramNode pn, string orig_dir, string dir, string publish_dir, string SourceFileName)
        {
            if (Directory.Exists(publish_dir))
                Directory.Delete(publish_dir, true);
            Directory.CreateDirectory(publish_dir);
            StringBuilder sb = new StringBuilder();
            string framework = "net9.0";
            if (comp_opt.target == TargetType.WinExe)
            {
                framework = "net9.0-windows";
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\">");
                sb.AppendLine("<PropertyGroup><PublishAot>true</PublishAot><PublishTrimmed>true</PublishTrimmed><OutputType>WinExe</OutputType><TargetFramework>" + framework + "</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup>");
                sb.AppendLine("<ItemGroup><Reference Include = \"" + an.Name + "\"><HintPath>" + Path.Combine(dir, an.Name) + ".dll" + "</HintPath></Reference></ItemGroup>");
                sb.AppendLine("</Project>");
            }
            else if (comp_opt.target == TargetType.Dll)
            {
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
                sb.AppendLine("<PropertyGroup><PublishAot>true</PublishAot><PublishTrimmed>true</PublishTrimmed><OutputType>Library</OutputType><TargetFramework>" + framework + "</TargetFramework></PropertyGroup>");
                sb.AppendLine("<ItemGroup><Reference Include = \"" + an.Name + "\"><HintPath>" + Path.Combine(dir, an.Name) + ".dll" + "</HintPath></Reference></ItemGroup>");
                sb.AppendLine("</Project>");
            }
            else
            {
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
                sb.AppendLine("<PropertyGroup><PublishAot>true</PublishAot><PublishTrimmed>true</PublishTrimmed><OutputType>Exe</OutputType><TargetFramework>" + framework + "</TargetFramework></PropertyGroup>");
                sb.AppendLine("<ItemGroup><Reference Include = \"" + an.Name + "\"><HintPath>" + Path.Combine(dir, an.Name) + ".dll" + "</HintPath></Reference></ItemGroup>");
                sb.AppendLine("</Project>");
            }

            string csproj = Path.Combine(dir, Path.GetFileNameWithoutExtension(an.Name)+"___native" + ".csproj");
            File.WriteAllText(csproj, sb.ToString());
            sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("namespace StartApp");
            sb.AppendLine("{");
            sb.AppendLine("class StartProgram");
            sb.AppendLine("{");
            if (comp_opt.target == TargetType.Dll)
            {
                List<ICommonNamespaceFunctionNode> dll_export_methods = new List<ICommonNamespaceFunctionNode>();
                foreach (ICommonNamespaceFunctionNode cnfn in pn.namespaces[pn.namespaces.Length-2].functions)
                {
                    if (cnfn.Attributes != null)
                        foreach (IAttributeNode attr in cnfn.Attributes)
                        {
                            if (attr.AttributeType.name == "DllExportAttribute")
                            {
                                dll_export_methods.Add(cnfn);
                                break;
                            }
                        }
                }
                foreach (ICommonNamespaceFunctionNode cnfn in dll_export_methods)
                {
                    sb.AppendLine("[UnmanagedCallersOnly(EntryPoint = \""+cnfn.name+"\")]");
                    sb.Append("public static ");
                    sb.Append(helper.GetTypeReference(cnfn.return_value_type).tp);
                    sb.Append(" ");
                    sb.Append(cnfn.name);
                    sb.Append("(");
                    for (int i=0; i<cnfn.parameters.Length; i++)
                    {
                        if (i > 0)
                            sb.Append(",");
                        var tp = helper.GetTypeReference(cnfn.parameters[i].type).tp;
                        sb.Append(tp.FullName);
                        sb.Append(" ");
                        sb.Append(cnfn.parameters[i].name);
                    }
                    sb.AppendLine(")");
                    sb.AppendLine("{");
                    sb.AppendLine("return "+cnfn.comprehensive_namespace.namespace_name + "." + cnfn.comprehensive_namespace.namespace_name + "." + cnfn.name);
                    sb.Append("(");
                    for (int i = 0; i < cnfn.parameters.Length; i++)
                    {
                        if (i > 0)
                            sb.Append(",");
                        sb.Append(cnfn.parameters[i].name);
                    }
                    sb.AppendLine(");");
                    sb.AppendLine("}");
                }
                
            }
            else
            {
                sb.AppendLine("static void Main(string[] args)");
                sb.AppendLine("{");
                sb.AppendLine(entry_meth.DeclaringType.FullName + "." + entry_meth.Name + "();");
                sb.AppendLine("}");
            }
                
            sb.AppendLine("}");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "Program.cs"), sb.ToString());
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo = new System.Diagnostics.ProcessStartInfo();
            p.StartInfo.FileName = "dotnet";
            string runtime = "win-x64";
            if (comp_opt.platformtarget == CompilerOptions.PlatformTarget.dotnetlinuxnative)
                runtime = "linux-x64";
            else if (comp_opt.platformtarget == CompilerOptions.PlatformTarget.dotnetmacosnative)
                runtime = "osx.10.11-x64";
            string conf = "Debug";
           // if (comp_opt.dbg_attrs == DebugAttributes.Release)
                conf = "Release";
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = true;
            if (comp_opt.target == TargetType.Dll)
                p.StartInfo.Arguments = "publish -f " + framework + " --runtime " + runtime + " -c " + conf + " /p:NativeLib=Shared --self-contained true " + csproj;
            else
                p.StartInfo.Arguments = "publish -f " + framework + " --runtime " + runtime + " -c " + conf + " --self-contained true " + csproj;
            p.Start();
            p.WaitForExit();
            try
            {
                var files = Directory.GetFiles(Path.Combine(dir, "bin" + Path.DirectorySeparatorChar + conf + Path.DirectorySeparatorChar + framework + Path.DirectorySeparatorChar + runtime + Path.DirectorySeparatorChar + "publish"));
                foreach (var file in files)
                    File.Copy(file, Path.Combine(publish_dir, Path.GetFileName(file)));
                foreach (var file in files)
                    File.Copy(file, Path.Combine(orig_dir, Path.GetFileName(file).Replace("___native", "")), true);
            }
            catch (Exception ex)
            {
                throw new TreeConverter.SaveAssemblyError(ex.Message, new TreeRealization.location(0, 0, 0, 0, SourceFileName));
            }
        }

        //Метод, переводящий семантическое дерево в сборку .NET
        public void ConvertFromTree(SemanticTree.IProgramNode p, string TargetFileName, string SourceFileName, CompilerOptions options, string[] ResourceFiles)
        {
            //SystemLibrary.SystemLibInitializer.RestoreStandardFunctions();
            string fname = TargetFileName;
            var onlyfname = System.IO.Path.GetFileName(fname);
            comp_opt = options;
            ad = Thread.GetDomain(); //получаем домен приложения
            an = new Mono.Cecil.AssemblyNameDefinition("Program1.exe", new Version("1.0.0.0")); //создаем имя сборки
            string dir = Directory.GetCurrentDirectory();
            string orig_dir = null;
            string dotnet_publish_dir = null;
            string source_name = fname;//p.Location.document.file_name;
            int pos = source_name.LastIndexOf(Path.DirectorySeparatorChar);
            if (pos != -1) //если имя файла указано с путем, то выделяем
            {
                dir = source_name.Substring(0, pos + 1);
                //an.CodeBase = String.Concat("file:///", source_name.Substring(0, pos));
                source_name = source_name.Substring(pos + 1);
            }
            string name = source_name.Substring(0, source_name.LastIndexOf('.'));
            if (comp_opt.target == TargetType.Exe || comp_opt.target == TargetType.WinExe)
                an.Name = name;// + ".exe";
            else an.Name = name; //+ ".dll";

            //if (name == "PABCRtl")
            //{
            //    pabc_rtl_converted = true;
            //    an.Flags = AssemblyNameFlags.PublicKey;
            //    an.VersionCompatibility = System.Configuration.Assemblies.AssemblyVersionCompatibility.SameProcess;
            //    an.HashAlgorithm = System.Configuration.Assemblies.AssemblyHashAlgorithm.None;
            //    FileStream publicKeyStream = File.Open(Path.Combine(Path.GetDirectoryName(TargetFileName), name == "PABCRtl" ? "PublicKey.snk" : "PublicKey32.snk"), FileMode.Open);
            //    byte[] publicKey = new byte[publicKeyStream.Length];
            //    publicKeyStream.Read(publicKey, 0, (int)publicKeyStream.Length);
            //    // Provide the assembly with a public key.
            //    an.SetPublicKey(publicKey);
            //    publicKeyStream.Close();
            //}
            if (IsDotnet5())
            {
                orig_dir = dir;
                dir = Path.Combine(dir, an.Name + "_dotnet5");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                dotnet_publish_dir = Path.Combine(dir, "publish");
                dir = Path.Combine(dir, "tmp");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            else if (IsDotnetNative())
            {
                orig_dir = dir;
                dir = Path.Combine(dir, an.Name + "_dotnetnative");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                dotnet_publish_dir = Path.Combine(dir, "publish");
                dir = Path.Combine(dir, "tmp");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }

            ab = Mono.Cecil.AssemblyDefinition.CreateAssembly(an, "Program1", Mono.Cecil.ModuleKind.Console);//определяем сборку
            
            //int nn = ad.GetAssemblies().Length;
            if (options.NeedDefineVersionInfo)
            {
                //ab.DefineVersionInfoResource(options.Product, options.ProductVersion, options.Company,
                //    options.Copyright, options.TradeMark);
                has_unmanaged_resources = true;
            }
            //if (options.MainResourceFileName != null)
            //{
            //    try
            //    {
            //        ab.DefineUnmanagedResource(options.MainResourceFileName);
            //        has_unmanaged_resources = true;
            //    }
            //    catch
            //    {
            //        throw new TreeConverter.SourceFileError(options.MainResourceFileName);
            //    }
            //}
            //else if (options.MainResourceData != null)
            //{
            //    try
            //    {
            //        ab.DefineUnmanagedResource(options.MainResourceData);
            //        has_unmanaged_resources = true;
            //    }
            //    catch
            //    {
            //        throw new TreeConverter.SourceFileError("");
            //    }
            //}
            save_debug_info = comp_opt.dbg_attrs == DebugAttributes.Debug || comp_opt.dbg_attrs == DebugAttributes.ForDebugging;
            add_special_debug_variables = comp_opt.dbg_attrs == DebugAttributes.ForDebugging;

            //bool emit_sym = true;
            if (save_debug_info) //если модуль отладочный, то устанавливаем атрибут, запрещающий inline методов
            {
                var customAttr = new Mono.Cecil.CustomAttribute(ab.MainModule.ImportReference(TypeFactory.DebuggableAttributeCtor), new byte[] { 0x01, 0x00, 0x01, 0x01, 0x00, 0x00 });

                ab.CustomAttributes.Add(customAttr);
            }


            if (!IsDotnet5() && !IsDotnetNative() && (comp_opt.target == TargetType.Exe || comp_opt.target == TargetType.WinExe))
                mb = ab.MainModule; //определяем модуль (save_debug_info - флаг включать отладочную информацию)
            else
                mb = ab.MainModule;

            helper = new Helper(mb);
            TypeFactory.Init(mb);
            NETGeneratorTools.Init(mb);

            cur_unit = Path.GetFileNameWithoutExtension(SourceFileName);
            string entry_cur_unit = cur_unit;
            
            if (comp_opt.target != TargetType.Dll)
            {
                entry_type = new Mono.Cecil.TypeDefinition(cur_unit, "Program", TypeAttributes.Public, mb.TypeSystem.Object);
                mb.Types.Add(entry_type);
            }

            // SSM 07.02.20
            if (entry_type != null)
                cur_type = entry_type;
            //точка входа в приложение
            if (p.main_function != null)
            {
                ConvertFunctionHeader(p.main_function);
                entry_meth = helper.GetMethod(p.main_function).mi as Mono.Cecil.MethodDefinition;
                cur_meth = entry_meth;
                il = cur_meth.Body.GetILProcessor();
                if (options.target != TargetType.Dll && options.dbg_attrs == DebugAttributes.ForDebugging)
                    AddSpecialInitDebugCode();
            }
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            Mono.Cecil.MethodDefinition tmp_meth = cur_meth;

            //при отладке компилятора здесь иногда ничего нет!
            ICommonNamespaceNode[] cnns = p.namespaces;


            //создаем отладочные документы
            if (save_debug_info)
            {
                first_doc = new Mono.Cecil.Cil.Document(SourceFileName)
                {
                    Type = Mono.Cecil.Cil.DocumentType.Text,
                    Language = Mono.Cecil.Cil.DocumentLanguage.Pascal,
                    LanguageVendor = Mono.Cecil.Cil.DocumentLanguageVendor.Microsoft
                };
                
                sym_docs.Add(SourceFileName, first_doc);
                for (int iii = 0; iii < cnns.Length; iii++)
                {
                    string cnns_document_file_name = null;
                    if (cnns[iii].Location != null)
                    {
                        cnns_document_file_name = cnns[iii].Location.file_name;
                        doc = new Mono.Cecil.Cil.Document(cnns_document_file_name)
                        {
                            Type = Mono.Cecil.Cil.DocumentType.Text,
                            Language = Mono.Cecil.Cil.DocumentLanguage.Pascal,
                            LanguageVendor = Mono.Cecil.Cil.DocumentLanguageVendor.Microsoft
                        };
                    }
                    else
                        doc = first_doc;
                    if (cnns_document_file_name != null && !sym_docs.ContainsKey(cnns_document_file_name))
                        sym_docs.Add(cnns_document_file_name, doc);//сохраняем его в таблице документов
                }
                first_doc = sym_docs[cnns[0].Location == null ? SourceFileName : cnns[0].Location.file_name];

                if (p.main_function != null)
                {
                    if (p.main_function.function_code is IStatementsListNode)
                        EntryPointLocation = ((IStatementsListNode)p.main_function.function_code).LeftLogicalBracketLocation;
                    else
                        EntryPointLocation = p.main_function.function_code.Location;
                }
                else
                    EntryPointLocation = null;
            }
            ICommonNamespaceNode entry_ns = null;

            //Переводим заголовки типов
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                bool is_main_namespace = cnns[iii].namespace_name == "" && comp_opt.target != TargetType.Dll || comp_opt.target == TargetType.Dll && cnns[iii].namespace_name == "";
                ICommonNamespaceNode cnn = cnns[iii];
                // SSM 07.02.20
                if (entry_type != null)
                    cur_type = entry_type;
                if (!is_main_namespace)
                { 
                    cur_unit = cnn.namespace_name; // SSM 05.02.20 here change
                    if (IsDllAndSystemNamespace(cur_unit, onlyfname))
                        cur_unit = "$" + cur_unit;
                }
                else
                    cur_unit = entry_cur_unit;
                if (iii == cnns.Length - 1 && comp_opt.target != TargetType.Dll || comp_opt.target == TargetType.Dll && iii == cnns.Length - 1)
                    entry_ns = cnn;
                ConvertTypeHeaders(cnn.types);
            }

            //Переводим псевдоинстанции generic-типов
            foreach (ICommonTypeNode ictn in p.generic_type_instances)
            {
                ConvertTypeHeaderInSpecialOrder(ictn);
            }

            Dictionary<ICommonNamespaceNode, Mono.Cecil.TypeDefinition> NamespacesTypes = new Dictionary<ICommonNamespaceNode, Mono.Cecil.TypeDefinition>();

            for (int iii = 0; iii < cnns.Length; iii++)
            {
                bool is_main_namespace = cnns[iii].namespace_name == "" && comp_opt.target != TargetType.Dll || comp_opt.target == TargetType.Dll && cnns[iii].namespace_name == "";
                if (!is_main_namespace)
                {
                    // SSM 05.02.20 here change
                    var cnnsnamespace_name = cnns[iii].namespace_name;
                    if (IsDllAndSystemNamespace(cnnsnamespace_name, onlyfname))
                        cnnsnamespace_name = "$" + cnnsnamespace_name;
                    //определяем синтетический класс для модуля
                    cur_type = new Mono.Cecil.TypeDefinition(cnnsnamespace_name, cnns[iii].namespace_name, TypeAttributes.Public, mb.TypeSystem.Object);
                    mb.Types.Add(cur_type);
                    types.Add(cur_type);
                    NamespaceTypesList.Add(cur_type);
                    NamespacesTypes.Add(cnns[iii], cur_type);
                    if (cnns[iii].IsMain)
                    {   // SSM 05.02.20 here change
                        var attr_class = new Mono.Cecil.TypeDefinition(cnnsnamespace_name, "$GlobAttr", TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                        mb.Types.Add(attr_class);

                        var attr_constr = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                        attr_class.Methods.Add(attr_constr);
                        var il = attr_constr.Body.GetILProcessor();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.AttributeCtor));
                        il.Emit(OpCodes.Ret);

                        var customAttr = new Mono.Cecil.CustomAttribute(attr_constr);
                        cur_type.CustomAttributes.Add(customAttr);
                    }
                    else if (!IsDotnetNative())
                    {
                        var attr_class = new Mono.Cecil.TypeDefinition(cnnsnamespace_name, "$ClassUnitAttr", TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                        mb.Types.Add(attr_class);

						var attr_constr = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
						attr_class.Methods.Add(attr_constr);
                        var il = attr_constr.Body.GetILProcessor();
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.AttributeCtor));
                        il.Emit(OpCodes.Ret);

						var customAttr = new Mono.Cecil.CustomAttribute(attr_constr);
						cur_type.CustomAttributes.Add(customAttr);
                    }
                }
                else
                {
                    // SSM 07.02.20
                    if (entry_type != null)
                        NamespacesTypes.Add(cnns[iii], entry_type);
                }

            }

            if (comp_opt.target == TargetType.Dll)
            {
                for (int iii = 0; iii < cnns.Length; iii++)
                {
                    string tmp = cur_unit;
                    if (cnns[iii].namespace_name != "")
                    {
                        cur_unit = cnns[iii].namespace_name; // SSM 05.02.20 here change
                        if (IsDllAndSystemNamespace(cur_unit, onlyfname))
                            cur_unit = "$" + cur_unit;
                    }
                    else
                        cur_unit = entry_cur_unit;
                    foreach (ITemplateClass tc in cnns[iii].templates)
                    {
                        CreateTemplateClass(tc);
                    }
                    cur_unit = tmp;
                }
                for (int iii = 0; iii < cnns.Length; iii++)
                {
                    string tmp = cur_unit;
                    if (cnns[iii].namespace_name != "")
                    {
                        cur_unit = cnns[iii].namespace_name; // SSM 05.02.20 here change
                        if (IsDllAndSystemNamespace(cur_unit, onlyfname))
                            cur_unit = "$" + cur_unit;
                    }
                    else
                        cur_unit = entry_cur_unit;
                    foreach (ITypeSynonym ts in cnns[iii].type_synonims)
                    {
                        CreateTypeSynonim(ts);
                    }
                    cur_unit = tmp;
                }
            }
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                cur_type = NamespacesTypes[cnns[iii]];
                cur_unit_type = NamespacesTypes[cnns[iii]];

                var methodDef = new Mono.Cecil.MethodDefinition("$static_init$", MethodAttributes.Public | MethodAttributes.Static, mb.TypeSystem.Void);
                cur_unit_type.Methods.Add(methodDef);

                methodDef.Body.GetILProcessor().Emit(OpCodes.Ret);
                helper.AddDummyMethod(cur_unit_type, methodDef);
                ConvertTypeMemberHeaders(cnns[iii].types);
			}
            //Переводим псевдоинстанции generic-типов
            foreach (IGenericTypeInstance ictn in p.generic_type_instances)
            {
                ConvertGenericInstanceTypeMembers(ictn);
            }

            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                cur_type = NamespacesTypes[cnns[iii]];
                cur_unit_type = NamespacesTypes[cnns[iii]];
                ConvertFunctionHeaders(cnns[iii].functions, false);
            }

            //Переводим псевдоинстанции функций
            foreach (IGenericFunctionInstance igfi in p.generic_function_instances)
            {
                ConvertGenericFunctionInstance(igfi);
            }

            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                cur_type = NamespacesTypes[cnns[iii]];
                cur_unit_type = NamespacesTypes[cnns[iii]];
                ConvertFunctionHeaders(cnns[iii].functions, true);
            }

            if (p.InitializationCode != null)
            {
                tmp_il = il;
                if (entry_meth != null)
                {
                    il = entry_meth.Body.GetILProcessor();
                    ConvertStatement(p.InitializationCode);
                }
                il = tmp_il;
            }

            

            
            /*foreach (var item in non_local_variables)
            {
                tmp_il = il;
                il = item.Value.Item2.GetILGenerator();
                ConvertNonLocalVariables(item.Key.var_definition_nodes, item.Value.Item1);
                il = tmp_il;
            }*/


            Mono.Cecil.MethodDefinition unit_cci = null;

            //Переводим заголовки всего остального (процедур, переменных)
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                bool is_main_namespace = iii == cnns.Length - 1 && comp_opt.target != TargetType.Dll;
                ICommonNamespaceNode cnn = cnns[iii];
                string tmp_unit_name = cur_unit;
                if (!is_main_namespace)
                {
                    cur_unit = cnn.namespace_name;
                    if (IsDllAndSystemNamespace(cur_unit, onlyfname))
                        cur_unit = "$" + cur_unit;
                }
                else
                    cur_unit = entry_cur_unit;
                cur_type = NamespacesTypes[cnn];

                //ConvertFunctionHeaders(cnn.functions);
                
                if (!is_main_namespace)
                {
                    //определяем статический конструктор класса для модуля
                    var constrDef = new Mono.Cecil.MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                    cur_type.Methods.Add(constrDef);

                    il = constrDef.Body.GetILProcessor();
					if (cnn.IsMain) unit_cci = constrDef;
					ModulesInitILGenerators.Add(cur_type, il);
                    
                    //перводим константы
                    ConvertNamespaceConstants(cnn.constants);
                    //переводим глобальные переменные модуля
                    ConvertGlobalVariables(cnn.variables);
                    ConvertNamespaceEvents(cnn.events);
                    //il.Emit(OpCodes.Ret);
                }
                else
                {
                    //Не нарвится мне порядок вызова. надо с этим разобраться
                    init_variables_mb = helper.GetMethodBuilder(cnn.functions[cnn.functions.Length-1]);// cur_type.DefineMethod("$InitVariables", MethodAttributes.Public | MethodAttributes.Static);
                    il = entry_meth.Body.GetILProcessor();
                    ModulesInitILGenerators.Add(cur_type, il);
                    il = init_variables_mb.Body.GetILProcessor();
                    //перводим константы
                    ConvertNamespaceConstants(cnn.constants);
                    ConvertGlobalVariables(cnn.variables);
                    ConvertNamespaceEvents(cnn.events);
                    il = entry_meth.Body.GetILProcessor();
                    //il.Emit(OpCodes.Ret);
                }

                cur_unit = tmp_unit_name;
            }

            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                cur_type = NamespacesTypes[cnns[iii]];
                cur_unit_type = NamespacesTypes[cnns[iii]];
                //генерим инциализацию для полей
                foreach (SemanticTree.ICommonTypeNode ctn in cnns[iii].types)
                    GenerateInitCodeForFields(ctn);
            }

            if (p.InitializationCode != null)
            {
                tmp_il = il;
                if (entry_meth == null)
                {
                    il = unit_cci.Body.GetILProcessor();
                    ConvertStatement(p.InitializationCode);
                }
                il = tmp_il;
            }
            // SSM 07.02.20
            if (entry_type != null)
                cur_type = entry_type;
            //is_in_unit = false;
            //переводим реализации
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                bool is_main_namespace = iii == 0 && comp_opt.target != TargetType.Dll;
                ICommonNamespaceNode cnn = cnns[iii];
                string tmp_unit_name = cur_unit;
                if (!is_main_namespace)
                {
                    cur_unit = cnn.namespace_name; // SSM 05.02.20 here change
                    if (IsDllAndSystemNamespace(cur_unit, onlyfname))
                        cur_unit = "$" + cur_unit;
                }
                //if (iii > 0) is_in_unit = true;
                cur_unit_type = NamespacesTypes[cnns[iii]];
                cur_type = cur_unit_type;
                ConvertTypeImplementations(cnn.types);
                ConvertFunctionsBodies(cnn.functions);
                cur_unit = tmp_unit_name;
            }
            if (comp_opt.target != TargetType.Dll && p.main_function != null)
            {
                cur_unit_type = NamespacesTypes[cnns[0]];
                cur_type = cur_unit_type;
                ConvertBody(p.main_function.function_code);
            }
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                if (save_debug_info) doc = sym_docs[cnns[iii].Location == null ? SourceFileName : cnns[iii].Location.file_name];
                cur_type = NamespacesTypes[cnns[iii]];
                cur_unit_type = NamespacesTypes[cnns[iii]];
                //вставляем ret в int_meth
                foreach (SemanticTree.ICommonTypeNode ctn in cnns[iii].types)
                    GenerateRetForInitMeth(ctn);
                ModulesInitILGenerators[cur_type].Emit(OpCodes.Ret);
            }
            for (int iii = 0; iii < cnns.Length; iii++)
            {
                MakeAttribute(cnns[iii]);
            }
            doc = first_doc;
            // SSM 07.02.20
            if (entry_type != null)
                cur_type = entry_type;

            CloseTypes();//закрываем типы
            switch (comp_opt.target)
            {
                case TargetType.Exe: ab.EntryPoint = entry_meth; break;
                case TargetType.WinExe:
                    if (!comp_opt.ForRunningWithEnvironment)
						ab.EntryPoint = entry_meth;
                    else
						ab.EntryPoint = entry_meth; break;
			}

            /**/
            try
            { //ne osobo vazhnaja vesh, sohranjaet v exe-shnik spisok ispolzuemyh prostranstv imen, dlja strahovki obernuli try catch

                if (comp_opt.dbg_attrs == DebugAttributes.ForDebugging)
                {
                    string[] namespaces = p.UsedNamespaces;

                    var attr_class = new Mono.Cecil.TypeDefinition("", "$UsedNsAttr", TypeAttributes.Public | TypeAttributes.BeforeFieldInit, mb.ImportReference(TypeFactory.AttributeType));
                    mb.Types.Add(attr_class);

                    var fld_ns = new Mono.Cecil.FieldDefinition("ns", FieldAttributes.Public, mb.TypeSystem.String);
                    attr_class.Fields.Add(fld_ns);

                    var fld_count = new Mono.Cecil.FieldDefinition("count", FieldAttributes.Public, mb.TypeSystem.Int32);
                    attr_class.Fields.Add(fld_count);

                    var attr_ci = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                    attr_ci.HasThis = true;
                    attr_ci.Parameters.Add(new Mono.Cecil.ParameterDefinition(mb.TypeSystem.Int32));
                    attr_ci.Parameters.Add(new Mono.Cecil.ParameterDefinition(mb.TypeSystem.String));
                    attr_class.Methods.Add(attr_ci);

                    var attr_il = attr_ci.Body.GetILProcessor();
                    attr_il.Emit(OpCodes.Ldarg_0);
                    attr_il.Emit(OpCodes.Ldarg_1);
                    attr_il.Emit(OpCodes.Stfld, fld_count);
                    attr_il.Emit(OpCodes.Ldarg_0);
                    attr_il.Emit(OpCodes.Ldarg_2);
                    attr_il.Emit(OpCodes.Stfld, fld_ns);
                    attr_il.Emit(OpCodes.Ret);

                    int len = 2 + 2 + 4 + 1;
                    foreach (string ns in namespaces)
                    {
                        len += ns.Length + 1;
                    }
                    byte[] bytes = new byte[len];
                    bytes[0] = 1;
                    bytes[1] = 0;
                    using (BinaryWriter bw = new BinaryWriter(new MemoryStream()))
                    {
                        bw.Write(namespaces.Length);
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (string ns in namespaces)
                        {
                            sb.Append(Convert.ToChar(ns.Length));
                            sb.Append(ns);
                            //bw.Write(ns);
                        }
                        if (sb.Length > 127)
                        {
                            len += 1;
                            bytes = new byte[len];
                            bytes[0] = 1;
                            bytes[1] = 0;
                        }
                        bw.Write(sb.ToString());
                        bw.Seek(0, SeekOrigin.Begin);
                        bw.BaseStream.Read(bytes, 2, len - 4);
                        if (sb.Length > 127)
                        {
                            bytes[7] = (byte)(sb.Length & 0xFF);
                            bytes[6] = (byte)(0x80 | ((sb.Length & 0xFF00) >> 8));
                        }
                    }
                    var customAttr = new Mono.Cecil.CustomAttribute(attr_ci, bytes);
                    // SSM 07.02.20  ?
                    entry_type?.CustomAttributes.Add(customAttr);
                }
            }
            catch (Exception e)
            {

            }
            if (an.Name == "PABCRtl")
            {
                var constrRef = TypeFactory.AssemblyKeyFileAttributeCtor;

                CustomAttributeBuilder cab = CustomAttributeBuilder
                    .GetInstance(mb.ImportReference(TypeFactory.AssemblyKeyFileAttributeCtor))
                    .AddConstructorArgs(new object[] { an.Name == "PABCRtl" ? "PublicKey.snk" : "PublicKey32.snk" });

                ab.CustomAttributes.Add(cab.Build());

                cab = CustomAttributeBuilder
                    .GetInstance(mb.ImportReference(TypeFactory.AssemblyDelaySignAttributeCtor))
                    .AddConstructorArgs(new object[] { true });

                ab.CustomAttributes.Add(cab.Build());

                cab = CustomAttributeBuilder
                    .GetInstance(mb.ImportReference(TypeFactory.TargetFrameworkAttributeCtor))
                    .AddConstructorArgs(new object[] { ".NETFramework,Version=v4.0" });
                
                ab.CustomAttributes.Add(cab.Build());
            }

            var cab2 = CustomAttributeBuilder
                .GetInstance(mb.ImportReference(TypeFactory.SecurityRulesAttributeCtor))
                .AddConstructorArgs(new object[] { SecurityRuleSet.Level2 })
                .AddPropertyArgs(
                    new Mono.Cecil.PropertyReference[] { mb.ImportReference(TypeFactory.SecurityRulesAttributeSkipVerificationInFullTrustProperty) },
                    new object[] { true }
                );

            ab.CustomAttributes.Add(cab2.Build());

            if (entry_meth != null && comp_opt.target == TargetType.WinExe)
            {
                entry_meth.CustomAttributes.Add(
                    new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.STAThreadAttributeCtor), new byte[] { 0x01, 0x00, 0x00, 0x00 })
                );
            }
            List<FileStream> ResStreams = new List<FileStream>();
            if (ResourceFiles != null)
                foreach (string resname in ResourceFiles)
                {
                    FileStream stream = File.OpenRead(resname);
                    ResStreams.Add(stream);

                    var embeddedRes = new Mono.Cecil.EmbeddedResource(Path.GetFileName(resname), Mono.Cecil.ManifestResourceAttributes.Public, stream);
                    mb.Resources.Add(embeddedRes);
                }

            ab.CustomAttributes.Add(
                new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.CompilationRelaxationsAttributeCtor), new byte[] { 0x01, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 })
            );

            cab2 = CustomAttributeBuilder
                .GetInstance(mb.ImportReference(TypeFactory.AssemblyTitleAttributeCtor))
                .AddConstructorArgs(new object[] { options.Title });

            ab.CustomAttributes.Add(cab2.Build());


            cab2 = CustomAttributeBuilder
                .GetInstance(mb.ImportReference(TypeFactory.AssemblyDescriptionAttributeCtor))
                .AddConstructorArgs(new object[] { options.Description });

            ab.CustomAttributes.Add(cab2.Build());
            
            if (options.TargetFramework != "")
            {
                string frameworkVersion = string.Join(".", options.TargetFramework.Substring(3).AsEnumerable());

                cab2 = CustomAttributeBuilder
                    .GetInstance(mb.ImportReference(TypeFactory.TargetFrameworkAttributeCtor))
                    .AddConstructorArgs(new object[] { $".NETFramework,Version=v{frameworkVersion}" });

                ab.CustomAttributes.Add(cab2.Build());
            }

            int tries = 0;
            bool not_done = true;
            do
            {
                try
                {
                    if (comp_opt.target == TargetType.Exe || comp_opt.target == TargetType.WinExe)
                    {
                        if (IsDotnet5() || IsDotnetNative())
                            ab.Write(an.Name + ".dll");
                        else if (comp_opt.platformtarget == NETGenerator.CompilerOptions.PlatformTarget.x86)
                        {
                            ab.MainModule.Attributes |= Mono.Cecil.ModuleAttributes.Required32Bit;
                            ab.MainModule.Architecture = Mono.Cecil.TargetArchitecture.I386; 
                            ab.Write(an.Name + ".exe");
                        }
                                
                        //else if (comp_opt.platformtarget == NETGenerator.CompilerOptions.PlatformTarget.x64)
                        //    ab.Save(an.Name + ".exe", PortableExecutableKinds.PE32Plus, ImageFileMachine.IA64);
                        else ab.Write(an.Name + ".exe");
                        //сохраняем сборку
                        if (IsDotnet5())
                            BuildDotnet5(orig_dir, dir, dotnet_publish_dir);
                        else if (IsDotnetNative())
                            BuildDotnetNative(p, orig_dir, dir, dotnet_publish_dir, SourceFileName);
                    }
                    else
                    {
                        if (comp_opt.platformtarget == NETGenerator.CompilerOptions.PlatformTarget.x86)
                        {
                            ab.MainModule.Attributes |= Mono.Cecil.ModuleAttributes.Required32Bit;
                            ab.MainModule.Architecture = Mono.Cecil.TargetArchitecture.I386;
                            ab.Write(an.Name + ".dll");
                        }
                                
                        //else if (comp_opt.platformtarget == NETGenerator.CompilerOptions.PlatformTarget.x64)
                        //    ab.Save(an.Name + ".dll", PortableExecutableKinds.PE32Plus, ImageFileMachine.IA64);
                        else ab.Write(an.Name + ".dll");
                        if (IsDotnetNative())
                            BuildDotnetNative(p, orig_dir, dir, dotnet_publish_dir, SourceFileName);
                    }
                    not_done = false;
                }
                catch (System.Runtime.InteropServices.COMException e)
                {
                    throw new TreeConverter.SaveAssemblyError(e.Message, new TreeRealization.location(0, 0, 0, 0, SourceFileName));
                }
                catch (System.IO.IOException e)
                {
                    if (tries < num_try_save)
                    {
                        if (has_unmanaged_resources)
                            throw new TreeConverter.SaveAssemblyError(e.Message, new TreeRealization.location(0, 0, 0, 0, SourceFileName));   
                        tries++;
                    }
                    else
                        throw new TreeConverter.SaveAssemblyError(e.Message, new TreeRealization.location(0, 0, 0, 0, SourceFileName));
                }
            }
            while (not_done);

            foreach (FileStream fs in ResStreams)
                fs.Close();
        }

        public void EmitAssemblyRedirects(AssemblyResolveScope resolveScope, string targetAssemblyPath)
        {
            if (IsDotnet5() || IsDotnetNative()) return;
            var appConfigPath = targetAssemblyPath + ".config";
            AppConfigUtil.UpdateAppConfig(resolveScope.CalculateBindingRedirects(), appConfigPath);
        }

        private void AddSpecialInitDebugCode()
        {
            //il.Emit(OpCodes.Call,typeof(Console).GetMethod("ReadLine"));
            //il.Emit(OpCodes.Pop);
        }

        private void ConvertNamespaceConstants(INamespaceConstantDefinitionNode[] Constants)
        {
            foreach (INamespaceConstantDefinitionNode Constant in Constants)
                ConvertConstantDefinitionNode(Constant, Constant.name, Constant.type, Constant.constant_value);
        }

        private void ConvertNamespaceEvents(ICommonNamespaceEventNode[] Events)
        {
            foreach (ICommonNamespaceEventNode Event in Events)
                Event.visit(this);
        }

        private void ConvertCommonFunctionConstantDefinitions(ICommonFunctionConstantDefinitionNode[] Constants)
        {
            foreach (ICommonFunctionConstantDefinitionNode Constant in Constants)
                //ConvertFunctionConstantDefinitionNode(Constant);
                ConvertConstantDefinitionNode(Constant, Constant.name, Constant.type, Constant.constant_value);
        }

        private void ConvertConstantDefinitionNode(IConstantDefinitionNode cnst, string name, ITypeNode type, IConstantNode constant_value)
        {
            if (constant_value is IArrayConstantNode)
                ConvertArrayConstantDef(cnst, name, type, constant_value);
            else
                if (constant_value is IRecordConstantNode || constant_value is ICompiledStaticMethodCallNodeAsConstant)
                    ConvertConstantDefWithInitCall(cnst, name, type, constant_value);
                else if (constant_value is ICommonNamespaceFunctionCallNodeAsConstant || constant_value is IBasicFunctionCallNodeAsConstant || constant_value is ICommonConstructorCallAsConstant || constant_value is ICompiledStaticFieldReferenceNodeAsConstant)
                    ConvertSetConstantDef(cnst, name, type, constant_value);
                else ConvertSimpleConstant(cnst, name, type, constant_value);
        }

        private void ConvertSetConstantDef(IConstantDefinitionNode cnst, string name, ITypeNode type, IConstantNode constant_value)
        {
            TypeInfo ti = helper.GetTypeReference(type);
            var attrs = FieldAttributes.Public | FieldAttributes.Static;
            if (comp_opt.target == TargetType.Dll)
                attrs |= FieldAttributes.InitOnly;

            var fb = new Mono.Cecil.FieldDefinition(name, attrs, ti.tp);
            cur_type.Fields.Add(fb);

            //il.Emit(OpCodes.Newobj, ti.tp.GetConstructor(Type.EmptyTypes));
            //il.Emit(OpCodes.Stsfld, fb);
            if (cnst != null)
                helper.AddConstant(cnst, fb);
            bool tmp = save_debug_info;
            save_debug_info = false;
            constant_value.visit(this);
            save_debug_info = tmp;
            il.Emit(OpCodes.Stsfld, fb);

            if (!ConvertedConstants.ContainsKey(constant_value))
                ConvertedConstants.Add(constant_value, fb);
        }

        private void ConvertSimpleConstant(IConstantDefinitionNode cnst, string name, ITypeNode type, IConstantNode constant_value)
        {
            var fb = new Mono.Cecil.FieldDefinition(name, FieldAttributes.Static | FieldAttributes.Public | FieldAttributes.Literal, helper.GetTypeReference(type).tp);
            cur_type.Fields.Add(fb);

            Mono.Cecil.TypeReference t = helper.GetTypeReference(type).tp;

            if (TypeIsEnum(t))
            {
                fb.HasDefault = true;
                fb.Constant = (constant_value as IEnumConstNode).constant_value;
            }
            else if (!(constant_value is INullConstantNode) && constant_value.value != null)
            {
                if (constant_value.value.GetType().FullName != t.FullName)
                {

                }
                else
                {
                    fb.HasDefault = true;
                    fb.Constant = constant_value.value;
                }
            }
            
        }

        private void PushConstantValue(IConstantNode cnst)
        {
            if (cnst is IIntConstantNode)
                PushIntConst((cnst as IIntConstantNode).constant_value);
            else if (cnst is IDoubleConstantNode)
                PushDoubleConst((cnst as IDoubleConstantNode).constant_value);
            else if (cnst is IFloatConstantNode)
                PushFloatConst((cnst as IFloatConstantNode).constant_value);
            else if (cnst is ICharConstantNode)
                PushCharConst((cnst as ICharConstantNode).constant_value);
            else if (cnst is IStringConstantNode)
                PushStringConst((cnst as IStringConstantNode).constant_value);
            else if (cnst is IByteConstantNode)
                PushByteConst((cnst as IByteConstantNode).constant_value);
            else if (cnst is ILongConstantNode)
                PushLongConst((cnst as ILongConstantNode).constant_value);
            else if (cnst is IBoolConstantNode)
                PushBoolConst((cnst as IBoolConstantNode).constant_value);
            else if (cnst is ISByteConstantNode)
                PushSByteConst((cnst as ISByteConstantNode).constant_value);
            else if (cnst is IUShortConstantNode)
                PushUShortConst((cnst as IUShortConstantNode).constant_value);
            else if (cnst is IUIntConstantNode)
                PushUIntConst((cnst as IUIntConstantNode).constant_value);
            else if (cnst is IULongConstantNode)
                PushULongConst((cnst as IULongConstantNode).constant_value);
            else if (cnst is IShortConstantNode)
                PushShortConst((cnst as IShortConstantNode).constant_value);
            else if (cnst is IEnumConstNode)
                PushIntConst((cnst as IEnumConstNode).constant_value);
            else if (cnst is INullConstantNode)
                il.Emit(cnst.type is IRefTypeNode ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);
        }

        private void ConvertConstantDefWithInitCall(IConstantDefinitionNode cnst, string name, ITypeNode type, IConstantNode constant_value)
        {
            TypeInfo ti = helper.GetTypeReference(type);
            var attrs = FieldAttributes.Public | FieldAttributes.Static;
            if (comp_opt.target == TargetType.Dll)
                attrs |= FieldAttributes.InitOnly;

            var fb = new Mono.Cecil.FieldDefinition(name, attrs, ti.tp);
            cur_type.Fields.Add(fb);

            if (cnst != null)
                helper.AddConstant(cnst, fb);
            bool tmp = save_debug_info;
            save_debug_info = false;
            AddInitCall(il, fb, ti.init_meth, constant_value);
            save_debug_info = tmp;
            if (!ConvertedConstants.ContainsKey(constant_value))
                ConvertedConstants.Add(constant_value, fb);
        }

        private void ConvertArrayConstantDef(IConstantDefinitionNode cnst, string name, ITypeNode type, IConstantNode constant_value)
        {
            //ConvertedConstants.ContainsKey(ArrayConstant)
            TypeInfo ti = helper.GetTypeReference(type);
            var attrs = FieldAttributes.Public | FieldAttributes.Static;
            if (comp_opt.target == TargetType.Dll)
                attrs |= FieldAttributes.InitOnly;

            var fb = new Mono.Cecil.FieldDefinition(name, attrs, ti.tp);
            if (cnst != null)
                helper.AddConstant(cnst, fb);
            CreateArrayGlobalVariable(il, fb, ti, constant_value as IArrayConstantNode, type);

            if (!ConvertedConstants.ContainsKey(constant_value))
                ConvertedConstants.Add(constant_value, fb);
        }

        //это требование Reflection.Emit - все типы должны быть закрыты
        private void CloseTypes()
        {
            //(ssyy) TODO: подумать, в каком порядке создавать типы
            List<Mono.Cecil.TypeDefinition> closed_types = new List<Mono.Cecil.TypeDefinition>();
            for (int i = 0; i < types.Count; i++)
                if (types[i].IsInterface)
                    try
                    {
                        //types[i].CreateType();
                    }
                    catch (TypeLoadException ex)
                    {
                        if (ex.Message.Contains("рекурсивное") || ex.Message.Contains("recursive") || ex.Message.Contains("rekursiv"))
                        {
                            SemanticTree.ICommonTypeNode ctn = helper.GetTypeNodeByTypeBuilder(types[i]);
                            if (ctn != null)
                                throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message, ctn.Location.file_name, ctn.Location.begin_line_num, ctn.Location.begin_column_num);
                        }
                    }

            for (int i = 0; i < enums.Count; i++)
                ;// nums[i].CreateType();
            List<Mono.Cecil.TypeDefinition> failed_types = new List<Mono.Cecil.TypeDefinition>();
            for (int i = 0; i < value_types.Count; i++)
            {
                try
                {
                    //value_types[i].CreateType();
                }
                catch (TypeLoadException ex)
                {
                    SemanticTree.ICommonTypeNode ctn = helper.GetTypeNodeByTypeBuilder(value_types[i]);
                    if (ctn != null)
                    {
                        if (ctn.is_generic_type_definition || ctn.ImplementingInterfaces != null && ctn.ImplementingInterfaces.Count > 0)
                        {
                            bool has_class_contrains = false;
                            if (ctn.generic_params != null)
                                foreach (var gp in ctn.generic_params)
                                {
                                    if (!(gp.base_type is ICommonTypeNode))
                                        continue;
                                    Mono.Cecil.TypeDefinition tb = helper.GetTypeReference(gp.base_type).tp as Mono.Cecil.TypeDefinition;
                                    if (tb != null)
                                    {
                                        try
                                        {
                                            //tb.CreateType();
                                            closed_types.Add(tb);
                                            has_class_contrains = true;
                                        }
                                        catch (TypeLoadException ex2)
                                        {

                                        }
                                    }
                                }
                            if (has_class_contrains)
                                continue;
                            else
                            {
                                failed_types.Add(value_types[i]);
                                continue;
                            }

                        }
                        else
                        {
                            bool has_meth_contrains = false;
                            foreach (var meth in ctn.methods)
                            {

                                if (meth.generic_params != null && meth.generic_params.Count > 0)
                                    foreach (var gp in meth.generic_params)
                                    {
                                        if (!(gp.base_type is ICommonTypeNode))
                                            continue;
                                        Mono.Cecil.TypeDefinition tb = helper.GetTypeReference(gp.base_type).tp as Mono.Cecil.TypeDefinition;
                                        if (tb != null && !closed_types.Contains(tb))
                                        {
                                            try
                                            {
                                                //tb.CreateType();
                                                closed_types.Add(tb);
                                                has_meth_contrains = true;
                                            }
                                            catch (TypeLoadException ex2)
                                            {
                                                throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message, ctn.Location.file_name, ctn.Location.begin_line_num, ctn.Location.begin_column_num);
                                            }
                                        }
                                    }
                            }
                            if (has_meth_contrains)
                                continue;
                            else
                            {
                                failed_types.Add(value_types[i]);
                                continue;
                            }
                        }
                        throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message, ctn.Location.file_name, ctn.Location.begin_line_num, ctn.Location.begin_column_num);
                    }
                    else
                        throw ex;
                }
            }

            for (int i = 0; i < types.Count; i++)
                if (!types[i].IsInterface && !closed_types.Contains(types[i]))
                {
                    try
                    {
                        //types[i].CreateType();
                    }
                    catch (TypeLoadException ex)
                    {
                        failed_types.Add(types[i]);
                    }
                }
            for (int i = 0; i < failed_types.Count; i++)
                try
                {
                    //failed_types[i].CreateType();
                }
                catch (TypeLoadException ex)
                {
                    SemanticTree.ICommonTypeNode ctn = helper.GetTypeNodeByTypeBuilder(failed_types[i]);
                    if (ctn != null)
                        throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message, ctn.Location.file_name, ctn.Location.begin_line_num, ctn.Location.begin_column_num);
                    else
                        throw ex;
                }

        }

        //перевод тела
        private void ConvertBody(IStatementNode body)
        {
            if (!(body is IStatementsListNode) && save_debug_info && body.Location != null)
                if (body.Location.begin_line_num == 0xFFFFFF) MarkSequencePoint(il, body.Location);
            body.visit(this);
            OptMakeExitLabel();
        }

        private void OptMakeExitLabel()
        {
            if (ExitProcedureCall)
            {
                il.Append(ExitLabel);
                ExitProcedureCall = false;
            }
        }

        //перевод заголовков типов
        private void ConvertTypeHeaders(ICommonTypeNode[] types)
        {
            foreach (ICommonTypeNode t in types)
            {
                ConvertTypeHeaderInSpecialOrder(t);
            }
        }

        private void CreateTemplateClass(ITemplateClass t)
        {
            if (t.serialized_tree != null)
            {
                var tb = new Mono.Cecil.TypeDefinition(cur_unit, "%" + t.name, TypeAttributes.Public);
                mb.Types.Add(tb);
                types.Add(tb);

                var cust_bldr = new Mono.Cecil.CustomAttribute(this.TemplateClassAttributeConstructor);
                cust_bldr.ConstructorArguments.Add(
                    new Mono.Cecil.CustomAttributeArgument(mb.TypeSystem.Byte.MakeArrayType(), t.serialized_tree)
				);

                tb.CustomAttributes.Add(cust_bldr);
            }
        }

        private void CreateTypeSynonim(ITypeSynonym t)
        {
            var tb = new Mono.Cecil.TypeDefinition(cur_unit, "%" + t.name, TypeAttributes.Public);
            mb.Types.Add(tb);
            types.Add(tb);
            add_possible_type_attribute(tb, t);
        }

        //TODO: возвращать деф ли реф?
        private Mono.Cecil.TypeReference CreateTypedFileType(ICommonTypeNode t)
        {
            var tt = helper.GetPascalTypeReference(t);
            if (tt != null) return tt;

            var tb = new Mono.Cecil.TypeDefinition(cur_unit, "%" + t.name, TypeAttributes.Public);
            mb.Types.Add(tb);
            types.Add(tb);
            helper.AddPascalTypeReference(t, tb);
            add_possible_type_attribute(tb, t);
            return tb;
        }

        //TODO: возвращать деф ли реф?
        private Mono.Cecil.TypeReference CreateTypedSetType(ICommonTypeNode t)
        {
            var tt = helper.GetPascalTypeReference(t);
            if (tt != null) return tt;

            var tb = new Mono.Cecil.TypeDefinition(cur_unit, "%" + t.name, TypeAttributes.Public);
            mb.Types.Add(tb);
            types.Add(tb);
            helper.AddPascalTypeReference(t, tb);
            add_possible_type_attribute(tb, t);
            return tb;
        }

        //TODO: возвращать деф ли реф?
        private Mono.Cecil.TypeDefinition CreateShortStringType(ITypeNode t)
        {
            var tb = new Mono.Cecil.TypeDefinition(cur_unit, "$string" + (uid++).ToString(), TypeAttributes.Public);
            types.Add(tb);
            add_possible_type_attribute(tb, t);
            return tb;
        }

        //переводим заголовки типов в порядке начиная с базовых классов (т. е. у которых наследники - откомпилированные типы)
        private void ConvertTypeHeaderInSpecialOrder(ICommonTypeNode t)
        {
            if (t.type_special_kind == type_special_kind.diap_type) return;
            if (t.type_special_kind == type_special_kind.array_kind) return;
            if (t.depended_from_indefinite) return;
            if (t.type_special_kind == type_special_kind.typed_file && comp_opt.target == TargetType.Dll)
            {
                if (!t.name.Contains(" "))
                {
                    CreateTypedFileType(t);
                    return;
                }
            }
            else
                if (t.type_special_kind == type_special_kind.set_type && comp_opt.target == TargetType.Dll)
                {
                    if (!t.name.Contains(" "))
                    {
                        CreateTypedSetType(t);
                        return;
                    }
                }

            if (helper.GetTypeReference(t) != null && !t.is_generic_parameter)
                return;
            helper.SetAsProcessing(t);
            if (t.is_generic_parameter)
            {
                //ConvertTypeHeaderInSpecialOrder(t.generic_container);
                AddTypeWithoutConvert(t);
                if (converting_generic_param != t)
                {
                    return;
                }
                converting_generic_param = null;
            }
            IGenericTypeInstance gti = t as IGenericTypeInstance;
            if (gti != null)
            {
                if (gti.original_generic is ICommonTypeNode)
                {
                    
                    ConvertTypeHeaderInSpecialOrder((ICommonTypeNode)gti.original_generic);
                }
                
                foreach (ITypeNode itn in gti.generic_parameters)
                {
                    if (itn is ICommonTypeNode && !itn.is_generic_parameter && !helper.IsProcessing(itn as ICommonTypeNode))
                    {
                        
                        ConvertTypeHeaderInSpecialOrder((ICommonTypeNode)itn);
                    }
                }
            }
            if (t.is_generic_type_definition)
            {
                AddTypeWithoutConvert(t);
                foreach (ICommonTypeNode par in t.generic_params)
                {
                    converting_generic_param = par;
                    ConvertTypeHeaderInSpecialOrder(par);
                }
            }
            else if ((t.type_special_kind == type_special_kind.none_kind ||
                t.type_special_kind == type_special_kind.record) && !t.IsEnum &&
                !t.is_generic_type_instance && !t.is_generic_parameter)
            {
                AddTypeWithoutConvert(t);
            }
            // ImplementingInterfacesOrEmpty, потому что если интерфейсы небыли лениво-посчитаны семантикой
            // То и тут их обходить нет смысла
            // А в ошибочных ситуациях (как err0303.pas) может ещё и зациклится
            foreach (ITypeNode interf in t.ImplementingInterfacesOrEmpty)
                if (!(interf is ICompiledTypeNode)  && (interf != t)) // SSM 15/02/23 (interf != t) добавил в связи с ковариантностью 
                                                                      // т.к. в случае IEnumerable<object> = IEnumerable<Student> возникал сбой
                    ConvertTypeHeaderInSpecialOrder((ICommonTypeNode)interf);
            if (t.base_type != null && !(t.base_type is ICompiledTypeNode))
            {
                ConvertTypeHeaderInSpecialOrder((ICommonTypeNode)t.base_type);
            }
            ConvertTypeHeader(t);
        }

        private void AddTypeWithoutConvert(ICommonTypeNode t)
        {
            if (helper.GetTypeReference(t) != null) return;
            Mono.Cecil.TypeDefinition tb = null; 
            try
            {
                var dotInd = t.name.IndexOf(".");
                var name = dotInd == -1 ? t.name : t.name.Substring(dotInd + 1);

                tb = new Mono.Cecil.TypeDefinition(cur_unit, name, ConvertAttributes(t));
                if (t.is_value_type)
                    tb.BaseType = mb.ImportReference(typeof(ValueType));
                mb.Types.Add(tb);

            }
            catch (ArgumentException ex)
            {
                if (ex.Message.IndexOf("fullname") != -1)
                    throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message.Replace("System.ArgumentException: ", ""), t.Location.file_name, t.Location.begin_line_num, t.Location.begin_column_num);
                throw ex;
            }
            
            helper.AddType(t, tb);
            //(ssyy) обрабатываем generics
            if (t.is_generic_type_definition)
            {
                int count = t.generic_params.Count;
                string[] par_names = new string[count];
                //Создаём массив имён параметров
                for (int i = 0; i < count; i++)
                {
                    par_names[i] = t.generic_params[i].name;
                }
                //Определяем параметры в строящемся типе
                var net_pars = par_names.Select(name => new Mono.Cecil.GenericParameter(name, tb)).ToArray();
                for (int i = 0; i < count; i++)
                {
                    tb.GenericParameters.Add(net_pars[i]);

                    //добавляем параметр во внутр. структуры
                    helper.AddExistingType(t.generic_params[i], net_pars[i]);
                }
            }
        }

        //перевод релизаций типов
        private void ConvertTypeImplementations(ICommonTypeNode[] types)
        {
            foreach (ICommonTypeNode t in types)
            //если это не особый тип переводим реализацию наверно здесь много лишнего нужно оставить ISimpleArrayNode
            {
                if ( t.type_special_kind != type_special_kind.diap_type &&
                    !t.depended_from_indefinite)
                    t.visit(this);
            }
        }

        private void ConvertTypeMemberHeaderAndRemoveFromList(ICommonTypeNode type, List<ICommonTypeNode> types)
        {
            if (!type.depended_from_indefinite)
            {
                if (type.type_special_kind == type_special_kind.array_wrapper &&
                    type.element_type.type_special_kind == type_special_kind.array_wrapper &&
                    type.element_type is ICommonTypeNode &&
                    types.IndexOf((ICommonTypeNode)(type.element_type)) > -1)
                {
                    ConvertTypeMemberHeaderAndRemoveFromList((ICommonTypeNode)(type.element_type), types);
                }
                ConvertTypeMemberHeader(type);
            }
            types.Remove(type);
        }

        //перевод заголовков членов класса
        private void ConvertTypeMemberHeaders(ICommonTypeNode[] types)
        {
            //(ssyy) Переупорядочиваем, чтобы массивы создавались в правильном порядке
            List<ICommonTypeNode> ts = new List<ICommonTypeNode>(types);
            while (ts.Count > 0)
            {
                ConvertTypeMemberHeaderAndRemoveFromList(ts[0], ts);
            }
            foreach (ICommonTypeNode t in types)
            {
                foreach (ICommonMethodNode meth in t.methods)
                {
                    if (meth.is_generic_function)
                    {
                        ConvertTypeInstancesMembersInFunction(meth);
                    }
                }
            }
        }

        private Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.TypeDefinition> added_types = new Dictionary<Mono.Cecil.TypeDefinition, Mono.Cecil.TypeDefinition>();
        private void BuildCloseTypeOrder(ICommonTypeNode value, Mono.Cecil.TypeDefinition tb)
        {
            foreach (ICommonClassFieldNode fld in value.fields)
            {
                ITypeNode ctn = fld.type;
                TypeInfo ti = helper.GetTypeReference(ctn);
                if (ctn is ICommonTypeNode && ti.tp.IsValueType && ti.tp.IsDefinition && tb != ti.tp)
                {
                    BuildCloseTypeOrder((ICommonTypeNode)ctn, (Mono.Cecil.TypeDefinition)ti.tp);
                }
            }
            if (!added_types.ContainsKey(tb))
            {
                value_types.Add(tb);
                added_types[tb] = tb;
            }
        }

        private Mono.Cecil.TypeReference GetTypeOfGenericInstanceField(Mono.Cecil.GenericInstanceType t, Mono.Cecil.FieldReference finfo)
        {
            if (finfo.FieldType.IsGenericParameter)
            {
                var param = (Mono.Cecil.GenericParameter)finfo.FieldType;
                return t.GenericArguments[param.Position];
            }
            else
            {
                return finfo.FieldType;
            }
        }

        private void ConvertGenericInstanceTypeMembers(IGenericTypeInstance value)
        {
            if (helper.GetTypeReference(value) == null)
            {
                return;
            }
            ICompiledGenericTypeInstance compiled_inst = value as ICompiledGenericTypeInstance;
            if (compiled_inst != null)
            {
                ConvertCompiledGenericInstanceTypeMembers(compiled_inst);
                return;
            }
            ICommonGenericTypeInstance common_inst = value as ICommonGenericTypeInstance;
            if (common_inst != null)
            {
                ConvertCommonGenericInstanceTypeMembers(common_inst);
                return;
            }
        }

        //ssyy 04.02.2010. Вернул следующие 2 функции в исходное состояние.
        private void ConvertCompiledGenericInstanceTypeMembers(ICompiledGenericTypeInstance value)
        {
            var t = (Mono.Cecil.GenericInstanceType)helper.GetTypeReference(value).tp;

            bool is_delegated_type = t.Resolve().BaseType?.IsFunctionPointer ?? false;
            foreach (IDefinitionNode dn in value.used_members.Keys)
            {
                ICompiledConstructorNode iccn = dn as ICompiledConstructorNode;
                if (iccn != null)
                {
                    var ci = mb.ImportReference(iccn.constructor_info).AsMemberOf(t);
                    helper.AddConstructor(value.used_members[dn] as IFunctionNode, ci);
                    continue;
                }
                ICompiledMethodNode icmn = dn as ICompiledMethodNode;
                if (icmn != null)
                {
                    if (is_delegated_type && mb.ImportReference(icmn.method_info).Resolve().IsSpecialName) continue;

                    var mi = mb.ImportReference(icmn.method_info).AsMemberOf(t);
                    helper.AddMethod(value.used_members[dn] as IFunctionNode, mi);

                    continue;
                }
                ICompiledClassFieldNode icfn = dn as ICompiledClassFieldNode;
                if (icfn != null)
                {
                    var ftype = GetTypeOfGenericInstanceField(t, mb.ImportReference(icfn.compiled_field));
                    var fi = mb.ImportReference(icfn.compiled_field).AsMemberOf(t);

                    helper.AddGenericField(value.used_members[dn] as ICommonClassFieldNode, fi, ftype, null);
                    continue;
                }
            }
        }

        private void ConvertCommonGenericInstanceTypeMembers(ICommonGenericTypeInstance value)
        {
            var t = (Mono.Cecil.GenericInstanceType)helper.GetTypeReference(value).tp;
            var genericInstances = new List<ICommonMethodNode>();
            Func<ICommonMethodNode, bool> processInstances = (icmn) =>
            {
                if (icmn.is_constructor)
                {
                    MethInfo mi = helper.GetConstructor(icmn);
                    if (mi != null)
                    {
                        var cnstr = mi.cnstr;
                        var ci = cnstr.AsMemberOf(t);
                        helper.AddConstructor(value.used_members[icmn] as IFunctionNode, ci);
                    }
                    return true;
                }
                else
                {
                    var methtmp = helper.GetMethod(icmn);
                    if (methtmp == null)
                        return true;
                    var meth = methtmp.mi;
                    if (meth.IsGenericInstance)
                        meth = meth.GetElementMethod();
                    var mi = meth.AsMemberOf(t);
                    helper.AddMethod(value.used_members[icmn] as IFunctionNode, mi);
                    return true;
                }
            };
            foreach (IDefinitionNode dn in value.used_members.Keys)
            {
                ICommonMethodNode icmn = dn as ICommonMethodNode;
                if (icmn != null)
                {
                    if (icmn.comperehensive_type.is_generic_type_instance)
                    {
                        genericInstances.Add(icmn);
                        continue;
                    }

                    if (processInstances(icmn))
                        continue;
                }
                ICommonClassFieldNode icfn = dn as ICommonClassFieldNode;
                if (icfn != null)
                {
                    FldInfo fldinfo = helper.GetField(icfn);
#if DEBUG
                    /*if (fldinfo == null)
                    {
                        fldinfo = fldinfo;
                    } */
#endif
                    if (fldinfo == null)
                        continue;
                    if (!(fldinfo is GenericFldInfo))
                    {
                        var finfo = fldinfo.fi;
                        var ftype = GetTypeOfGenericInstanceField(t, finfo);
                        var fi = finfo.AsMemberOf(t);
                        helper.AddGenericField(value.used_members[dn] as ICommonClassFieldNode, fi, ftype, finfo); // передаю также старое finfo чтобы на следующей итерации вызовом finfo.AsMemberOf(t) сконструировать правильное fi
					}
                    else
                    {
                        /* Вот этот код не выполняется ни в одном тесте и в примере 
                          type
                          Base<T> = class
                            XYZW: T;
                          end;
  
                          Derived<T1> = class(Base<T1>)
                          end;

                        begin
                          var a := new Derived<integer>;
                          a.XYZW := 2;
                        end.
                        срабатывает неправильно !!!

                        Исправил, введя доп. поле в GenericFldInfo, которое хранит FieldBuilder и позволяет конструировать fi на следующей итерации
                        */

                        var finfo = (fldinfo as GenericFldInfo).prev_fi;
                        var fi = finfo.AsMemberOf(t);
                        helper.AddGenericField(value.used_members[dn] as ICommonClassFieldNode, fi, (fldinfo as GenericFldInfo).field_type, finfo); 
                        //FieldInfo finfo = fldinfo.fi;
                        //FieldInfo fi = finfo;
                        //helper.AddGenericField(value.used_members[dn] as ICommonClassFieldNode, fi, (fldinfo as GenericFldInfo).field_type, finfo); 
                        //#if DEBUG

                        /*{
                            var f = File.AppendText("d:\\aa.txt");
                            f.WriteLine(DateTime.Now);
                            f.WriteLine($"{value.used_members[dn] as ICommonClassFieldNode}  {fi}  {(fldinfo as GenericFldInfo).field_type}");
                            f.Close();
                        }*/
                        //#endif
                    }
                    continue;
                }
            }

            foreach (ICommonMethodNode icmn in genericInstances)
            {
                processInstances(icmn);
            }
        }

        private object[] get_constants(IConstantNode[] cnsts)
        {
            object[] objs = new object[cnsts.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                if (cnsts[i] is IArrayConstantNode)
                {
                    List<object> lst = new List<object>();
                    var arr_cnst = cnsts[i] as IArrayConstantNode;
                    foreach (IConstantNode cn in arr_cnst.ElementValues)
                        lst.Add(cn.value);
                    objs[i] = lst.ToArray();
                }
                else if (cnsts[i] is ITypeOfOperatorAsConstant)
                    objs[i] = helper.GetTypeReference((cnsts[i] as ITypeOfOperatorAsConstant).TypeOfOperator.oftype).tp;
                else
                    objs[i] = cnsts[i].value;
            }
            return objs;
        }

        private Mono.Cecil.PropertyReference[] get_named_properties(IPropertyNode[] props)
        {
            var arr = new Mono.Cecil.PropertyReference[props.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                if (props[i] is ICompiledPropertyNode)
                    arr[i] = mb.ImportReference((props[i] as ICompiledPropertyNode).property_info);
                else
                    arr[i] = helper.GetProperty(props[i]).prop;
            }
            return arr;
        }

        private Mono.Cecil.FieldReference[] get_named_fields(IVAriableDefinitionNode[] fields)
        {
			Mono.Cecil.FieldReference[] arr = new Mono.Cecil.FieldReference[fields.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                if (fields[i] is ICompiledClassFieldNode)
                    arr[i] = mb.ImportReference((fields[i] as ICompiledClassFieldNode).compiled_field);
                else
                    arr[i] = helper.GetField(fields[i] as ICommonClassFieldNode).fi;
            }
            return arr;
        }

        private void MakeAttribute(ICommonNamespaceNode cnn)
        {
            IAttributeNode[] attrs = cnn.Attributes;
            for (int i = 0; i < attrs.Length; i++)
            {
                var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                var attr = CustomAttributeBuilder.GetInstance(ctor)
                    .AddConstructorArgs(get_constants(attrs[i].Arguments))
                    .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                    .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                    .Build();

                ab.CustomAttributes.Add(attr);
			}
        }

        private void MakeAttribute(ICommonTypeNode ctn)
        {
            var t = (Mono.Cecil.TypeDefinition)helper.GetTypeReference(ctn).tp;
            IAttributeNode[] attrs = ctn.Attributes;
            for (int i = 0; i < attrs.Length; i++)
            {
                try
                {
                    var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                    var attr = CustomAttributeBuilder.GetInstance(ctor)
                        .AddConstructorArgs(get_constants(attrs[i].Arguments))
                        .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                        .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                        .Build();

                    t.CustomAttributes.Add(attr);
                }
                catch (ArgumentException ex)
                {
                    throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message.Replace("System.ArgumentException: ", ""), attrs[i].Location.file_name, attrs[i].Location.begin_line_num, attrs[i].Location.begin_column_num);
                }
            }
        }

        private void MakeAttribute(ICommonPropertyNode prop)
        {
            var pb = (Mono.Cecil.PropertyDefinition)helper.GetProperty(prop).prop;
            IAttributeNode[] attrs = prop.Attributes;
            for (int i = 0; i < attrs.Length; i++)
            {
                var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                var attr = CustomAttributeBuilder.GetInstance(ctor)
                    .AddConstructorArgs(get_constants(attrs[i].Arguments))
                    .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                    .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                    .Build();

                pb.CustomAttributes.Add(attr);
            }
        }

        private void MakeAttribute(ICommonClassFieldNode fld)
        {
            var fb = (Mono.Cecil.FieldDefinition)helper.GetField(fld).fi;
            IAttributeNode[] attrs = fld.Attributes;
            for (int i = 0; i < attrs.Length; i++)
            {
                try
                {
                    var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                    var attr = CustomAttributeBuilder.GetInstance(ctor)
                        .AddConstructorArgs(get_constants(attrs[i].Arguments))
                        .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                        .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                        .Build();

                    fb.CustomAttributes.Add(attr);
                }
                catch (ArgumentException ex)
                {
                    throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message.Replace("System.ArgumentException: ", ""), attrs[i].Location.file_name, attrs[i].Location.begin_line_num, attrs[i].Location.begin_column_num);
                }
            }
        }

        private void MakeAttribute(ICommonParameterNode prm)
        {
            var pb = helper.GetParameter(prm).pb;
            IAttributeNode[] attrs = prm.Attributes;
            for (int i = 0; i < attrs.Length; i++)
            {
                try
                {
					var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

					var attr = CustomAttributeBuilder.GetInstance(ctor)
						.AddConstructorArgs(get_constants(attrs[i].Arguments))
						.AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
						.AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
						.Build();

					pb.CustomAttributes.Add(attr);
                }
                catch (ArgumentException ex)
                {
                    throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message.Replace("System.ArgumentException: ", ""), attrs[i].Location.file_name, attrs[i].Location.begin_line_num, attrs[i].Location.begin_column_num);
                }
            }
        }

        private void MakeAttribute(ICommonFunctionNode func)
        {
            var mb = helper.GetMethod(func).mi as Mono.Cecil.MethodDefinition;
            IAttributeNode[] attrs = func.Attributes;
            List<Mono.Cecil.CustomAttribute> returnValueAttrs = new List<Mono.Cecil.CustomAttribute>();
            for (int i = 0; i < attrs.Length; i++)
            {
                var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? this.mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                var attr = CustomAttributeBuilder.GetInstance(ctor)
                    .AddConstructorArgs(get_constants(attrs[i].Arguments))
                    .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                    .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                    .Build();

                if (attrs[i].qualifier == SemanticTree.attribute_qualifier_kind.return_kind)
                {
                    if (attrs[i].Arguments.Length > 0 && helper.GetTypeReference(attrs[i].AttributeType).tp.FullName == "System.Runtime.InteropServices.MarshalAsAttribute")
                    try
                    {
                        mb.MethodReturnType.MarshalInfo.NativeType = (Mono.Cecil.NativeType)attrs[i].Arguments[0].value;
                    }
                    catch(ArgumentException ex)
                    {
                        throw new PascalABCCompiler.Errors.CommonCompilerError(ex.Message.Replace(", переданный для DefineUnmanagedMarshal,",""), attrs[i].Location.file_name, attrs[i].Location.begin_line_num, attrs[i].Location.begin_column_num);
                    }
                    else
                    {
                        returnValueAttrs.Add(attr);
                    }
                }
                else
                    mb.CustomAttributes.Add(attr);
            }
            if (returnValueAttrs.Count > 0)
            {
                foreach (var attr in returnValueAttrs)
                    mb.MethodReturnType.CustomAttributes.Add(attr);
            }
            foreach (IParameterNode pn in func.parameters)
            {
                ParamInfo pi = helper.GetParameter(pn);
                if (pi == null) continue;
                var pb = pi.pb;
                attrs = pn.Attributes;
                for (int i = 0; i < attrs.Length; i++)
                {
                    var ctor = (attrs[i].AttributeConstructor is ICompiledConstructorNode) ? this.mb.ImportReference((attrs[i].AttributeConstructor as ICompiledConstructorNode).constructor_info) : helper.GetConstructor(attrs[i].AttributeConstructor).cnstr;

                    var attr = CustomAttributeBuilder.GetInstance(ctor)
                        .AddConstructorArgs(get_constants(attrs[i].Arguments))
                        .AddPropertyArgs(get_named_properties(attrs[i].PropertyNames), get_constants(attrs[i].PropertyInitializers))
                        .AddFieldArgs(get_named_fields(attrs[i].FieldNames), get_constants(attrs[i].FieldInitializers))
                        .Build();

                    pb.CustomAttributes.Add(attr);
                }
            }
        }

        //определяем заголовки членов класса
        private void ConvertTypeMemberHeader(ICommonTypeNode value)
        {
            //если это оболочка над массивом переводим ее особым образом
            if (value.type_special_kind == type_special_kind.diap_type || value.type_special_kind == type_special_kind.array_kind) return;
            if (value.fields.Length == 1 && value.fields[0].type is ISimpleArrayNode)
            {
                ConvertArrayWrapperType(value);
                return;
            }
            if (value is ISimpleArrayNode) return;
            //этот тип уже был переведен, поэтому находим его
            TypeInfo ti = helper.GetTypeReference(value);

            //ivan
            if (ti.tp.Resolve().IsEnum || !(ti.tp.IsDefinition)) return;
            var tb = (Mono.Cecil.TypeDefinition)ti.tp;
            if (tb.IsValueType)
                BuildCloseTypeOrder(value, tb);
            //сохраняем контекст
            TypeInfo tmp_ti = cur_ti;
            cur_ti = ti;
            var tmp = cur_type;
            cur_type = tb;

            //(ssyy) Если это интерфейс, то пропускаем следующую хрень
            if (!value.IsInterface)
            {
                //определяем метод $Init$ для выделения памяти, если метод еще не определен (в структурах он опред-ся раньше)
                Mono.Cecil.MethodDefinition clone_mb = null;
				Mono.Cecil.MethodDefinition ass_mb = null;
                if (ti.init_meth != null && tb.IsValueType)
                {
                    clone_mb = ti.clone_meth as Mono.Cecil.MethodDefinition;
                    ass_mb = ti.assign_meth as Mono.Cecil.MethodDefinition;
                }
                foreach (ICommonClassFieldNode fld in value.fields)
                    fld.visit(this);

                foreach (ICommonMethodNode meth in value.methods)
                    ConvertMethodHeader(meth);
                foreach (ICommonPropertyNode prop in value.properties)
                    prop.visit(this);

                foreach (IClassConstantDefinitionNode constant in value.constants)
                    constant.visit(this);

                foreach (ICommonEventNode evnt in value.events)
                    evnt.visit(this);

                if (clone_mb != null)
                {
                    clone_mb.Body.GetILProcessor().Emit(OpCodes.Ldloc_0);
                    clone_mb.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
                if (ass_mb != null)
                {
                    ass_mb.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
                if (ti.fix_meth != null)
                {
                    ti.fix_meth.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
            }
            else
            {
                //(ssyy) сейчас переводим интерфейс

                foreach (ICommonMethodNode meth in value.methods)
                    ConvertMethodHeader(meth);
                foreach (ICommonPropertyNode prop in value.properties)
                    prop.visit(this);
                foreach (ICommonEventNode evnt in value.events)
                    evnt.visit(this);
            }

            if (value.default_property != null)
            {
                using (BinaryWriter bw = new BinaryWriter(new MemoryStream()))
                {
                    bw.Write(value.default_property.name);
                    bw.Seek(0, SeekOrigin.Begin);
                    byte[] bytes = new byte[2 + value.default_property.name.Length + 1 + 2];
                    bytes[0] = 1;
                    bytes[1] = 0;
                    bw.BaseStream.Read(bytes, 2, value.default_property.name.Length + 1);

                    var attrRef = mb.ImportReference(typeof(DefaultMemberAttribute));
                    Mono.Cecil.MethodReference attrCtor = attrRef.Resolve()
                        .GetConstructors()
                        .Single(item =>
                            item.Parameters.Count == 1
                            && item.Parameters[0].ParameterType.FullName == mb.TypeSystem.String.FullName
                        );

                    attrCtor = mb.ImportReference(attrCtor);

                    var customAttr = new Mono.Cecil.CustomAttribute(attrCtor, bytes);
                }
            }
            //восстанавливаем контекст
            cur_type = tmp;
            cur_ti = tmp_ti;
        }

        private bool NeedAddCloneMethods(ICommonTypeNode ctn)
        {
            foreach (ICommonClassFieldNode cfn in ctn.fields)
            {
                if (cfn.polymorphic_state != polymorphic_state.ps_static &&
                    (cfn.type.type_special_kind == type_special_kind.array_wrapper ||
                    cfn.type.type_special_kind == type_special_kind.base_set_type ||
                    cfn.type.type_special_kind == type_special_kind.short_string ||
                    cfn.type.type_special_kind == type_special_kind.text_file ||
                    cfn.type.type_special_kind == type_special_kind.typed_file ||
                    cfn.type.type_special_kind == type_special_kind.binary_file ||
                    cfn.type.type_special_kind == type_special_kind.set_type))
                    return true;
                if (cfn.polymorphic_state != polymorphic_state.ps_static && cfn.type.type_special_kind == type_special_kind.record && cfn.type is ICommonTypeNode)
                    if (NeedAddCloneMethods(cfn.type as ICommonTypeNode))
                        return true;
            }
            return false;
        }

        private void AddInitMembers(TypeInfo ti, Mono.Cecil.TypeDefinition tb, ICommonTypeNode ctn)
        {
            var init_mb = new Mono.Cecil.MethodDefinition("$Init$", MethodAttributes.Public, mb.TypeSystem.Void);
            tb.Methods.Add(init_mb);

            ti.init_meth = init_mb;
            //определяем метод $Init$ для выделения памяти, если метод еще не определен (в структурах он опред-ся раньше)
            //MethodBuilder init_mb = ti.init_meth;
            //if (init_mb == null) init_mb = tb.DefineMethod("$Init$", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            ti.init_meth = init_mb;
            //определяем метод Clone и Assign
            if (tb.IsValueType)
            {
                Mono.Cecil.MethodDefinition clone_mb = null;
                Mono.Cecil.MethodDefinition ass_mb = null;
                if (NeedAddCloneMethods(ctn))
                {
                    clone_mb = new Mono.Cecil.MethodDefinition("$Clone$", MethodAttributes.Public, tb);
                    tb.Methods.Add(clone_mb);
                    var lb = new Mono.Cecil.Cil.VariableDefinition(tb);
                    clone_mb.Body.Variables.Add(lb);
                    MarkSequencePoint(clone_mb.Body.GetILProcessor(), 0xFFFFFF, 0, 0xFFFFFF, 0);
                    clone_mb.Body.GetILProcessor().Emit(OpCodes.Ldloca, lb);
                    clone_mb.Body.GetILProcessor().Emit(OpCodes.Call, init_mb);
                    ti.clone_meth = clone_mb;

                    ass_mb = new Mono.Cecil.MethodDefinition("$Assign$", MethodAttributes.Public, mb.TypeSystem.Void);
                    tb.Methods.Add(ass_mb);
                    ass_mb.Parameters.Add
                    (
                        new Mono.Cecil.ParameterDefinition("$obj$", ParameterAttributes.None, tb)
                    );
                    ti.assign_meth = ass_mb;
                }
                var fix_mb = new Mono.Cecil.MethodDefinition("$Fix$", MethodAttributes.Public, mb.TypeSystem.Void);
                tb.Methods.Add(fix_mb);
                ti.fix_meth = fix_mb;
            }
        }

        private void AddTypeToCloseList(Mono.Cecil.TypeDefinition tb)
        {
            if (!tb.IsValueType) types.Add(tb);
        }

        private void AddEnumToCloseList(Mono.Cecil.TypeDefinition emb)
        {
            enums.Add(emb);
        }

        private Hashtable accessors_names = new Hashtable();

        private string GetPossibleAccessorName(IFunctionNode fn, out bool get_set)
        {
            get_set = false;
            return fn.name;
        }

        private bool has_com_import_attr(ICommonTypeNode value)
        {
            IAttributeNode[] attrs = value.Attributes;
            foreach (IAttributeNode attr in attrs)
                if (attr.AttributeType == SystemLibrary.SystemLibrary.comimport_type)
                    return true;
            return false;
        }

        //Переводит аттрибуты типа в аттрибуты .NET
        private TypeAttributes ConvertAttributes(ICommonTypeNode value)
        {
            TypeAttributes ta = TypeAttributes.Public;
            if (value.type_access_level == type_access_level.tal_internal)
                ta = TypeAttributes.NotPublic;
            //(ssyy) 27.10.2007 Прекратить бардак!  Я в третий раз устанавливаю здесь аттрибут Sealed!
            //Это надо, чтобы нельзя было наследовать от записей.
            //В следующий раз разработчику, снявшему аттрибут Sealed, указать причину, по которой это было сделано!
            if (value.is_value_type)
                ta |= TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit | TypeAttributes.Sealed;
            //TypeAttributes.Sealed нужно!

            if (value.IsSealed)
                ta |= TypeAttributes.Sealed;

            ICompiledTypeNode ictn = value.base_type as ICompiledTypeNode;
            if (ictn != null)
            {
                if (ictn.compiled_type.FullName == TypeFactory.MulticastDelegateType.FullName)
                {
                    ta |= TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.Sealed;
                }
            }
            if (value.IsInterface)
            {
                ta |= TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.AnsiClass;
            }
            if (value.IsAbstract)
            {
                ta |= TypeAttributes.Abstract;
            }
            return ta;
        }

        public void ConvertTypeInstancesInFunction(ICommonFunctionNode func)
        {
            List<IGenericTypeInstance> insts;
            bool flag = instances_in_functions.TryGetValue(func, out insts);
            if (!flag) return;
            foreach (IGenericTypeInstance igi in insts)
            {
                ConvertTypeHeaderInSpecialOrder(igi);
            }
        }

        public void ConvertTypeInstancesMembersInFunction(ICommonFunctionNode func)
        {
            List<IGenericTypeInstance> insts;
            bool flag = instances_in_functions.TryGetValue(func, out insts);
            if (!flag) return;
            foreach (IGenericTypeInstance igi in insts)
            {
                ConvertGenericInstanceTypeMembers(igi);
            }
        }

        private void AddPropertyAccessors(ICommonTypeNode ctn)
        {
            ICommonPropertyNode[] props = ctn.properties;
            foreach (ICommonPropertyNode prop in props)
            {
                if (prop.get_function != null && !prop_accessors.ContainsKey(prop.get_function))
                    prop_accessors.Add(prop.get_function, prop.get_function);
                if (prop.set_function != null && !prop_accessors.ContainsKey(prop.set_function))
                    prop_accessors.Add(prop.set_function, prop.set_function);
            }
            ICommonMethodNode[] meths = ctn.methods;
            foreach (ICommonMethodNode meth in meths)
            {
                if (meth.overrided_method != null && !prop_accessors.ContainsKey(meth) && prop_accessors.ContainsKey(meth.overrided_method))
                    prop_accessors.Add(meth, meth);
            }
        }

        private string BuildTypeName(string type_name)
        {
            if (type_name.IndexOf(".") == -1)
                return cur_unit + "." + type_name;
            return type_name;
        }


        private void SeparateTypeName(string fullTypeName, out string ns, out string name)
        {
            var ind = fullTypeName.IndexOf(".");
            if (ind == -1)
            {
                ns = cur_unit;
                name = fullTypeName;
            }
            else
            {
                ns = fullTypeName.Substring(0, ind);
                name = fullTypeName.Substring(ind + 1);
            }
        }

        //Перевод заголовка типа
        private void ConvertTypeHeader(ICommonTypeNode value)
        {
            //(ssyy) Обрабатываем инстанции generic-типов
            //FillGetterMethodsTable(value);
            IGenericTypeInstance igtn = value as IGenericTypeInstance;
            if (igtn != null)
            {
                //Формируем список типов-параметров 
                List<Mono.Cecil.TypeReference> iparams = new List<Mono.Cecil.TypeReference>();
                foreach (ITypeNode itn in igtn.generic_parameters)
                {
                    TypeInfo tinfo = helper.GetTypeReference(itn);
                    if (tinfo == null)
                    {
                        AddTypeInstanceToFunction(GetGenericFunctionContainer(value), igtn);
                        return;
                    }
                    iparams.Add(tinfo.tp);
                }
                //Запрашиваем инстанцию
                //ICompiledTypeNode icompiled_type = igtn.original_generic as ICompiledTypeNode;
                var orig_type = helper.GetTypeReference(igtn.original_generic).tp;
                var rez = orig_type.MakeGenericInstanceType(iparams.ToArray());
                //Добавляем в хэш
                TypeInfo inst_ti = helper.AddExistingType(igtn, rez);
                TypeInfo generic_def_ti = helper.GetTypeReference(igtn.original_generic);
                if (generic_def_ti.init_meth != null)
                    inst_ti.init_meth = generic_def_ti.init_meth.AsMemberOf(rez);
                if (generic_def_ti.clone_meth != null)
                    inst_ti.clone_meth = generic_def_ti.clone_meth.AsMemberOf(rez);
                if (generic_def_ti.assign_meth != null)
                    inst_ti.assign_meth = generic_def_ti.assign_meth.AsMemberOf(rez);
                return;
            }
            if (comp_opt.target == TargetType.Dll)
                AddPropertyAccessors(value);
            Mono.Cecil.TypeReference[] interfaces = new Mono.Cecil.TypeReference[value.ImplementingInterfaces.Count];
            for (int i = 0; i < interfaces.Length; i++)
            {
                TypeInfo ii_ti = helper.GetTypeReference(value.ImplementingInterfaces[i]);
                interfaces[i] = ii_ti.tp;
            }

            //определяем тип
            TypeInfo ti = helper.GetTypeReference(value);
            bool not_exist = ti == null;
            Mono.Cecil.TypeDefinition tb = null;
            Mono.Cecil.GenericParameter gtpb = null;
            if (!not_exist)
            {
                tb = ti.tp as Mono.Cecil.TypeDefinition;
                gtpb = ti.tp as Mono.Cecil.GenericParameter;
            }

            var ta = (not_exist) ? ConvertAttributes(value) : TypeAttributes.NotPublic;

            if (value.base_type is ICompiledTypeNode && (value.base_type as ICompiledTypeNode).compiled_type.FullName == TypeFactory.EnumType.FullName && gtpb == null)
            {
                ta = TypeAttributes.Public;
                if (value.type_access_level == type_access_level.tal_internal)
                    ta = TypeAttributes.NotPublic;

                SeparateTypeName(value.name, out var ns, out var name);

                var enumRef = mb.ImportReference(typeof(Enum));

                var emb = new Mono.Cecil.TypeDefinition(ns, name, ta, enumRef);
                mb.Types.Add(emb);

                var specField = new Mono.Cecil.FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, mb.TypeSystem.Int32);
                emb.Fields.Add(specField);

                //int num = 0;
                foreach (IClassConstantDefinitionNode ccfn in value.constants)
                {
                    var literal = new Mono.Cecil.FieldDefinition(ccfn.name, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, emb);
                    literal.HasDefault = true;
                    literal.Constant = (ccfn.constant_value as IEnumConstNode).constant_value;
                    emb.Fields.Add(literal);
                }
                AddEnumToCloseList(emb);
                helper.AddEnum(value, emb);
                return;
            }
            else
            {
                if (not_exist)
                {
                    SeparateTypeName(value.name, out var ns, out var name);
                    tb = new Mono.Cecil.TypeDefinition(ns, name, ta);
                    mb.Types.Add(tb);
                    
                    foreach (var item in interfaces)
                        tb.Interfaces.Add( new Mono.Cecil.InterfaceImplementation(item) );
                }
                else
                {
                    if (gtpb == null)
                    {
                        foreach (var interf in interfaces)
                        {
                            tb.Interfaces.Add(new Mono.Cecil.InterfaceImplementation(interf));
                        }
                    }
                    else
                    {
                        foreach (var item in interfaces)
                            gtpb.Constraints.Add(new Mono.Cecil.GenericParameterConstraint(item));

                        if (value.is_value_type)
                            gtpb.HasNotNullableValueTypeConstraint = true;
                        if (value.is_class)
                            gtpb.HasReferenceTypeConstraint = true;
                        if (value.methods.Length > 0)
                            gtpb.HasDefaultConstructorConstraint = true;
                    }
                }
            }
            //добавлям его во внутр. структуры
            if (not_exist)
            {
                ti = helper.AddType(value, tb);
            }
            //if (value.fields.Length == 1 && value.fields[0].type is ISimpleArrayNode)
            if (value.type_special_kind == type_special_kind.array_wrapper)
            {
                ti.is_arr = true;
            }
            if (value.base_type != null && !value.IsInterface)
            {
                var base_type = helper.GetTypeReference(value.base_type).tp;
                if (gtpb == null)
                {
                    tb.BaseType = base_type;
                }
                else
                {
                    if (base_type.FullName != mb.TypeSystem.Object.FullName)
                        gtpb.Constraints.Add(new Mono.Cecil.GenericParameterConstraint(base_type));
                }
            }
            if (!value.is_generic_parameter)
            {
                AddTypeToCloseList(tb);//добавляем его в список закрытия
                if (!value.IsInterface && value.type_special_kind != type_special_kind.array_wrapper)
                    AddInitMembers(ti, tb, value);
            }
            //если это обертка над массивом, сразу переводим реализацию
            //if (value.fields.Length == 1 && value.fields[0].type is ISimpleArrayNode) ConvertArrayWrapperType(value);
        }

        //перевод заголовков функций
        private void ConvertFunctionHeaders(ICommonNamespaceFunctionNode[] funcs, bool with_nested)
        {
            for (int i = 0; i < funcs.Length; i++)
            {
                if (!with_nested && funcs[i].functions_nodes != null && funcs[i].functions_nodes.Length > 0)
                    continue;
                if (with_nested && (funcs[i].functions_nodes == null || funcs[i].functions_nodes.Length == 0))
                    continue;
                IStatementsListNode sl = (IStatementsListNode)funcs[i].function_code;
                IStatementNode[] statements = sl.statements;
                if (statements.Length > 0 && statements[0] is IExternalStatementNode)
                {
                    //функция импортируется из dll
                    ICommonNamespaceFunctionNode func = funcs[i];
                    Mono.Cecil.TypeReference ret_type = null;
                    //получаем тип возвр. значения
                    if (func.return_value_type == null)
                        ret_type = null;//typeof(void);
                    else
                        ret_type = helper.GetTypeReference(func.return_value_type).tp;
                    Mono.Cecil.TypeReference[] param_types = GetParamTypes(func);//получаем параметры процедуры

                    IExternalStatementNode esn = (IExternalStatementNode)statements[0];
                    string module_name = Tools.ReplaceAllKeys(esn.module_name, StandartDirectories);

                    var methb = new Mono.Cecil.MethodDefinition(
                        func.name,
                        MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig,
                        ret_type
                    );
                    cur_type.Methods.Add(methb);
                    methb.CallingConvention = Mono.Cecil.MethodCallingConvention.Default;

                    //TODO разобраться с атрибутами и ссылкой на модуль
                    var moduleRef = new Mono.Cecil.ModuleReference(module_name);
                    mb.ModuleReferences.Add(moduleRef);

                    methb.PInvokeInfo = new Mono.Cecil.PInvokeInfo(
                        PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAnsi | PInvokeAttributes.BestFitDisabled,
                        esn.name,
                        moduleRef
                    );

                    helper.AddMethod(func, methb);
                    IParameterNode[] parameters = func.parameters;
                    //определяем параметры с указанием имени
                    for (int j = 0; j < parameters.Length; j++)
                    {
                        var pars = ParameterAttributes.None;
                        //if (func.parameters[j].parameter_type == parameter_type.var)
                        //  pars = ParameterAttributes.Out;

                        var pb = new Mono.Cecil.ParameterDefinition(parameters[j].name, pars, param_types[i]);
                        methb.Parameters.Add(pb);

                        helper.AddParameter(parameters[j], pb);
                    }
                }
                else
                    if (statements.Length > 0 && statements[0] is IPInvokeStatementNode)
                    {
                        //функция импортируется из dll
                        ICommonNamespaceFunctionNode func = funcs[i];
                        Mono.Cecil.TypeReference ret_type = null;
                        //получаем тип возвр. значения
                        if (func.return_value_type == null)
                            ret_type = null;//typeof(void);
                        else
                            ret_type = helper.GetTypeReference(funcs[i].return_value_type).tp;
                        Mono.Cecil.TypeReference[] param_types = GetParamTypes(funcs[i]);//получаем параметры процедуры
                        
                        var methb = new Mono.Cecil.MethodDefinition(
                            func.name,
                            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig,
                            ret_type
                        );
                        cur_type.Methods.Add(methb);
                        
                        // TODO странная ситуация с null
                        methb.PInvokeInfo = new Mono.Cecil.PInvokeInfo(PInvokeAttributes.BestFitDisabled, null, null);

                        helper.AddMethod(funcs[i], methb);
                        IParameterNode[] parameters = funcs[i].parameters;
                        //определяем параметры с указанием имени
                        for (int j = 0; j < parameters.Length; j++)
                        {
                            var pars = ParameterAttributes.None;
                            //if (func.parameters[j].parameter_type == parameter_type.var)
                            //  pars = ParameterAttributes.Out;
                            var pb = new Mono.Cecil.ParameterDefinition(parameters[j].name, pars, param_types[i]);
                            methb.Parameters.Add(pb);
                            helper.AddParameter(parameters[j], pb);
                        }
                    }
                    else
                        ConvertFunctionHeader(funcs[i]);
            }
            //(ssyy) 21.05.2008
            if (!with_nested)
            foreach (ICommonNamespaceFunctionNode ifn in funcs)
            {
                if (ifn.is_generic_function)
                {
                    ConvertTypeInstancesMembersInFunction(ifn);
                }
            }
        }

        //перевод тел функций
        private void ConvertFunctionsBodies(ICommonFunctionNode[] funcs)
        {
            for (int i = 0; i < funcs.Length; i++)
            {
                IStatementsListNode sl = (IStatementsListNode)funcs[i].function_code;
                /*if (sl.statements.Length > 0 && (sl.statements[0] is IExternalStatementNode))
                {
                    continue;
                }*/
                ConvertFunctionBody(funcs[i]);
            }
        }

        //создание записи активации для влож. процедур
        private Frame MakeAuxType(ICommonFunctionNode func)
        {

            var tb = new Mono.Cecil.TypeDefinition(null, "$" + func.name + "$" + uid++, TypeAttributes.NestedPublic);
            cur_type.NestedTypes.Add(tb);
            //определяем поле - ссылку на верхнюю запись активации
            var fb = new Mono.Cecil.FieldDefinition("$parent$", FieldAttributes.Public, tb.DeclaringType.IsValueType ? (Mono.Cecil.TypeReference)tb.DeclaringType.MakePointerType() : tb.DeclaringType);
            tb.Fields.Add(fb);
            //конструктор в кач-ве параметра, которого передается ссылка на верх. з/а
            Mono.Cecil.MethodDefinition cb = null;
            //определяем метод для инициализации
            var mb = new Mono.Cecil.MethodDefinition("$Init$", MethodAttributes.Private, this.mb.TypeSystem.Void);
            tb.Methods.Add(mb);
            if (funcs.Count > 0)
            {
                cb = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig, this.mb.TypeSystem.Void);
                cb.CallingConvention = Mono.Cecil.MethodCallingConvention.ThisCall;
                tb.Methods.Add(cb);

                cb.Parameters.Add(
                    new Mono.Cecil.ParameterDefinition("$parent$", ParameterAttributes.None, tb.DeclaringType.IsValueType ? (Mono.Cecil.TypeReference)tb.DeclaringType.MakeByReferenceType() : tb.DeclaringType)
                );
            }
            else
            {
                cb = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig, this.mb.TypeSystem.Void);
                tb.Methods.Add(cb);
            }
            var il = cb.Body.GetILProcessor();
            //сохраняем ссылку на верхнюю запись активации
            if (func is ICommonNestedInFunctionFunctionNode)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, fb);
            }
            //вызываем метод $Init$
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, mb);
            il.Emit(OpCodes.Ret);
            types.Add(tb);
            //создаем кадр записи активации
            Frame frm = new Frame();
            frm.cb = cb;
            frm.mb = mb;
            frm.tb = tb;
            frm.parent = fb;
            return frm;
        }

        //перевод псевдоинстанции функции
        private void ConvertGenericFunctionInstance(IGenericFunctionInstance igfi)
        {
            if (helper.GetMethod(igfi) != null)
                return;
            ICompiledMethodNode icm = igfi.original_function as ICompiledMethodNode;
            Mono.Cecil.MethodReference mi;
            if (icm != null)
            {
                mi = mb.ImportReference(icm.method_info);
            }
            else
            {
                MethInfo methi = helper.GetMethod(igfi.original_function);
                if (methi == null)//not used functions from pcu
                    return;
                mi = methi.mi;
            }
            int tcount = igfi.generic_parameters.Count;
            Mono.Cecil.TypeReference[] tpars = new Mono.Cecil.TypeReference[tcount];
            for (int i = 0; i < tcount; i++)
            {
                TypeInfo ti = helper.GetTypeReference(igfi.generic_parameters[i]);
                if (ti == null)
                    return;
                tpars[i] = ti.tp;
            }
            var rez = new Mono.Cecil.GenericInstanceMethod(mi);
            foreach (var genericArg in tpars)
                rez.GenericArguments.Add(genericArg);

            helper.AddMethod(igfi, rez);
        }

        //перевод заголовка функции
        private void ConvertFunctionHeader(ICommonFunctionNode func)
        {
            //if (is_in_unit && helper.IsUsed(func)==false) return;
            num_scope++; //увеличиваем глубину обл. видимости
            Mono.Cecil.TypeDefinition tb = null, tmp_type = cur_type;
            Frame frm = null;

            //func.functions_nodes.Length > 0 - имеет вложенные
            //funcs.Count > 0 - сама вложенная
            if (func.functions_nodes.Length > 0 || funcs.Count > 0)
            {
                frm = MakeAuxType(func);//создаем запись активации
                tb = frm.tb;
                cur_type = tb;
            }
            else tb = cur_type;
            var attrs = MethodAttributes.Public | MethodAttributes.Static;
            //определяем саму процедуру/функцию
            Mono.Cecil.MethodDefinition methb = null;
            methb = new Mono.Cecil.MethodDefinition(func.name, attrs, mb.TypeSystem.Void);
            tb.Methods.Add(methb);
            if (func.name == "__FixPointer" && cur_type.FullName == "PABCSystem.PABCSystem")
                fix_pointer_meth = methb;
            if (func.is_generic_function)
            {
                int count = func.generic_params.Count;
                for (int i = 0; i < count; i++)
                {
                    methb.GenericParameters.Add(
                        new Mono.Cecil.GenericParameter(func.generic_params[i].name, methb)
                    );
                }

                var genargs = methb.GenericParameters;
                for (int i = 0; i < count; i++)
                {
                    helper.AddExistingType(func.generic_params[i], genargs[i]);
                }
                foreach (ICommonTypeNode par in func.generic_params)
                {
                    converting_generic_param = par;
                    ConvertTypeHeaderInSpecialOrder(par);
                }
                ConvertTypeInstancesInFunction(func);
            }

            Mono.Cecil.TypeReference ret_type = null;
            //получаем тип возвр. значения
            if (func.return_value_type == null)
                ret_type = mb.TypeSystem.Void;
            else
                ret_type = helper.GetTypeReference(func.return_value_type).tp;
            //получаем типы параметров
            Mono.Cecil.TypeReference[] param_types = GetParamTypes(func);
            foreach (var paramType in param_types)
                methb.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            methb.ReturnType = ret_type;

            MethInfo mi = null;
            if (smi.Count != 0)
                //добавляем вложенную процедуру, привязывая ее к верхней процедуре
                mi = helper.AddMethod(func, methb, smi.Peek());
            else
                mi = helper.AddMethod(func, methb);
            mi.num_scope = num_scope;
            mi.disp = frm;//тип - запись активации
            smi.Push(mi);
            Mono.Cecil.ParameterDefinition pb = null;
            int num = 0;
            var tmp_il = il;
            il = methb.Body.GetILProcessor();

            if (save_debug_info)
            {
                if (func.function_code is IStatementsListNode)
                    MarkSequencePoint(((IStatementsListNode)func.function_code).LeftLogicalBracketLocation);
                else
                    MarkSequencePoint(func.function_code.Location);
            }

            //if (ret_type != typeof(void)) mi.ret_val = il.DeclareLocal(ret_type);
            //если функция вложенная, то добавляем фиктивный параметр
            //ссылку на верхнюю запись активации
            if (funcs.Count > 0)
            {
                mi.nested = true;//это вложенная процедура
                methb.Parameters[0].Name = "$up$";
                num = 1;
            }
            //все нелокальные параметры будем хранить в нестатических полях
            //записи активации. В начале функции инициализируем эти поля
            //параметрами
            IParameterNode[] parameters = func.parameters;
            Mono.Cecil.FieldDefinition[] fba = new Mono.Cecil.FieldDefinition[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                object default_value = null;
                if (parameters[i].default_value != null)
                {
                    default_value = helper.GetConstantForExpression(parameters[i].default_value);
                }
                var pa = ParameterAttributes.None;
                //if (func.parameters[i].parameter_type == parameter_type.var)
                //    pa = ParameterAttributes.Retval;
                if (default_value != null)
                    pa |= ParameterAttributes.Optional;
                pb = methb.Parameters[i + num];
                pb.Name = parameters[i].name;
                pb.Attributes = pa;

                if (parameters[i].is_params)
                    pb.CustomAttributes.Add(
                        new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.ParamArrayAttributeCtor),
                        new byte[] { 0x1, 0x0, 0x0, 0x0 })
                    );
                if (default_value != null)
                {
                    if ((Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX) && default_value.GetType().FullName != param_types[i + num].FullName && param_types[i + num].Resolve().IsEnum)
                        ;// default_value = Enum.ToObject(param_types[i + num], default_value);
                    pb.HasDefault = true;
                    if (default_value is TreeRealization.null_const_node) // SSM 20/04/21
                        pb.Constant = null;
                    else pb.Constant = default_value;
                }
                if (func.functions_nodes.Length > 0)
                {
                    Mono.Cecil.FieldDefinition fb = null;
                    //если параметр передается по значению, то все нормально
                    if (parameters[i].parameter_type == parameter_type.value)
                    {
                        fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, param_types[i + num]);
                        frm.tb.Fields.Add(fb);
                    } 
                    else
                    {
                        //иначе параметр передается по ссылке
                        //тогда вместо типа параметра тип& используем тип*
                        //например System.Int32& - System.Int32* (unmanaged pointer)
                        Mono.Cecil.TypeReference pt = param_types[i + num].GetElementType().MakePointerType();

                        //определяем поле для параметра
                        fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, pt);
                        frm.tb.Fields.Add(fb);
                    }

                    //добавляем как глобальный параметр
                    helper.AddGlobalParameter(parameters[i], fb).meth = smi.Peek();
                    fba[i] = fb;
                }
                else
                {
                    //если проца не содержит вложенных, то все хорошо
                    helper.AddParameter(parameters[i], pb).meth = smi.Peek();
                }
            }

            if (func is ICommonNamespaceFunctionNode && (func as ICommonNamespaceFunctionNode).ConnectedToType != null && !IsDotnetNative())
            {
                var attrTypeRef = mb.ImportReference(TypeFactory.ExtensionAttributeType);
                Mono.Cecil.MethodReference attrCtor = attrTypeRef.Resolve()
                    .GetConstructors()
                    .Single(item => item.Parameters.Count == 0);

                attrCtor = mb.ImportReference(attrCtor);

                if (!marked_with_extension_attribute.ContainsKey(cur_unit_type))
                {
                    cur_unit_type.CustomAttributes.Add(new Mono.Cecil.CustomAttribute(attrCtor, new byte[0]));
                    marked_with_extension_attribute[cur_unit_type] = cur_unit_type;
                }
				methb.CustomAttributes.Add(new Mono.Cecil.CustomAttribute(attrCtor, new byte[0]));
            }
            if (func.functions_nodes.Length > 0 || funcs.Count > 0)
            {
                //определяем переменную, хранящую ссылку на запись активации данной процедуры
                var frame = new Mono.Cecil.Cil.VariableDefinition(cur_type);
                il.Body.Variables.Add(frame);
                mi.frame = frame;
                if (doc != null) 
                    methb.DebugInformation.Scope.Variables.Add(
                        new Mono.Cecil.Cil.VariableDebugInformation(frame, "$disp$")
                     );
                if (funcs.Count > 0)
                {
                    //если она вложенная, то конструктору зап. акт. передаем ссылку на верх. з. а.
                    il.Emit(OpCodes.Ldarg_0);
                    //создаем запись активации
                    il.Emit(OpCodes.Newobj, frm.cb);
                    il.Emit(OpCodes.Stloc, frame);
                }
                else
                {
                    //в противном случае просто создаем з. а.
                    il.Emit(OpCodes.Newobj, frm.cb);
                    il.Emit(OpCodes.Stloc_0, frame);
                }
                if (func.functions_nodes.Length > 0)
                    for (int j = 0; j < fba.Length; j++)
                    {
                        //сохраняем нелокальные параметры в полях
                        il.Emit(OpCodes.Ldloc_0);
                        parameters = func.parameters;
                        if (parameters[j].parameter_type == parameter_type.value)
                        {
                            if (funcs.Count > 0) il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                            else il.Emit(OpCodes.Ldarg_S, (byte)j);
                        }
                        else
                        {
                            if (funcs.Count > 0) il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                            else il.Emit(OpCodes.Ldarg_S, (byte)j);
                        }
                        il.Emit(OpCodes.Stfld, fba[j]);
                    }
            }
            funcs.Add(func); //здесь наверное дублирование
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = methb;

            //если функция не содержит вложенных процедур, то
            //переводим переменные как локальные
            //if (func.functions_nodes.Length > 0)
            //    non_local_variables[func] = new Tuple<MethodBuilder, MethodBuilder, List<ICommonFunctionNode>>(frm.mb, methb, new List<ICommonFunctionNode>(funcs));
            if (func.functions_nodes.Length > 0)
                ConvertNonLocalVariables(func.var_definition_nodes, frm.mb);
            //переводим заголовки вложенных функций
            ConvertNestedFunctionHeaders(func.functions_nodes);
            //переводим тела вложенных функций
            //foreach (ICommonNestedInFunctionFunctionNode f in func.functions_nodes)
            //	ConvertFunctionBody(f);
            if (frm != null)
                frm.mb.Body.GetILProcessor().Emit(OpCodes.Ret);
            //восстанавливаем текущие значения
            cur_type = tmp_type;
            num_scope--;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
        }

        private bool IsVoidOrNull(ITypeNode tn)
        {
            if (tn == null)
                return true;
            if ((tn is ICompiledTypeNode) && (tn as ICompiledTypeNode).compiled_type.FullName == mb.TypeSystem.Void.FullName)
                return true;
            return false;
        }

        private void AddSpecialDebugVariables()
        {
            if (this.add_special_debug_variables)
            {
                Mono.Cecil.Cil.VariableDefinition spec_var = new Mono.Cecil.Cil.VariableDefinition(cur_unit_type);
                il.Body.Variables.Add(spec_var);
                il.Body.Method.DebugInformation.Scope.Variables.Add(
                    new Mono.Cecil.Cil.VariableDebugInformation(spec_var, "$class_var$0")
                );
            }
        }

        //перевод тела процедуры
        //(ssyy) По-моему, это вызывается только для вложенных процедур.
        private void ConvertFunctionBody(ICommonFunctionNode func)
        {
            //if (is_in_unit && helper.IsUsed(func)==false) return;
            num_scope++;
            MakeAttribute(func);
            IStatementsListNode sl = (IStatementsListNode)func.function_code;
            IStatementNode[] statements = sl.statements;
            if (sl.statements.Length > 0 && (statements[0] is IPInvokeStatementNode || (statements[0] is IExternalStatementNode)))
            {
                num_scope--;
                return;
            }
            MethInfo mi = helper.GetMethod(func);
            Mono.Cecil.TypeDefinition tmp_type = cur_type;
            if (mi.disp != null) cur_type = mi.disp.tb;
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = (Mono.Cecil.MethodDefinition)mi.mi;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            il = cur_meth.Body.GetILProcessor();
            smi.Push(mi);
            funcs.Add(func);
            ConvertCommonFunctionConstantDefinitions(func.constants);
            if (func.functions_nodes.Length == 0)
                ConvertLocalVariables(func.var_definition_nodes);

            foreach (ICommonNestedInFunctionFunctionNode f in func.functions_nodes)
                ConvertFunctionBody(f);
            //перевод тела
            if (func.name.IndexOf("<yield_helper_error_checkerr>") == -1)
                ConvertBody(func.function_code);
            else
                il.Emit(OpCodes.Ret);
            //ivan for debug
            if (save_debug_info)
            {
                AddSpecialDebugVariables();
            }
            //\ivan for debug
            if (func.return_value_type == null || func.return_value_type == SystemLibrary.SystemLibrary.void_type)
                il.Emit(OpCodes.Ret);
            cur_meth = tmp;
            cur_type = tmp_type;
            il = tmp_il;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
            num_scope--;
        }

        //перевод тела функции
        private void ConvertFunctionBody(ICommonFunctionNode func, MethInfo mi, bool conv_first_stmt)
        {
            //if (is_in_unit && helper.IsUsed(func)==false) return;
            num_scope++;
            Mono.Cecil.TypeDefinition tmp_type = cur_type;
            if (mi.disp != null) cur_type = mi.disp.tb;
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = (Mono.Cecil.MethodDefinition)mi.mi;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            il = cur_meth.Body.GetILProcessor();
            smi.Push(mi);
            funcs.Add(func);
            if (conv_first_stmt)
                ConvertBody(func.function_code);//переводим тело
            else
            {
                ConvertStatementsListWithoutFirstStatement(func.function_code as IStatementsListNode);
                OptMakeExitLabel();
            }
            //ivan for debug
            if (save_debug_info)
            {
                AddSpecialDebugVariables();
            }
            //\ivan for debug
            if (cur_meth.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Ret);
            //восстановление значений
            cur_meth = tmp;
            cur_type = tmp_type;
            il = tmp_il;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
            num_scope--;
        }

        private void ConvertNestedFunctionHeaders(ICommonNestedInFunctionFunctionNode[] funcs)
        {
            for (int i = 0; i < funcs.Length; i++)
                ConvertFunctionHeader(funcs[i]);
        }

        //процедура получения типов параметров процедуры
        private Mono.Cecil.TypeReference[] GetParamTypes(ICommonFunctionNode func)
        {
			Mono.Cecil.TypeReference[] tt = null;
            int num = 0;
            IParameterNode[] parameters = func.parameters;
            if (funcs.Count > 0)
            {
                tt = new Mono.Cecil.TypeReference[parameters.Length + 1];
                tt[num++] = cur_type.DeclaringType;
            }
            else
                tt = new Mono.Cecil.TypeReference[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                //этот тип уже был определен, поэтому получаем его с помощью хелпера
                Mono.Cecil.TypeReference tp = helper.GetTypeReference(parameters[i].type).tp;
                if (parameters[i].parameter_type == parameter_type.value)
                    tt[i + num] = tp;
                else
                    tt[i + num] = tp.MakeByReferenceType();
            }
            return tt;
        }

        private void ConvertNonLocalVariables(ILocalVariableNode[] vars, Mono.Cecil.MethodDefinition cb)
        {
            for (int i = 0; i < vars.Length; i++)
            {
                //если лок. переменная используется как нелокальная
                if (vars[i].is_used_as_unlocal == true)
                    ConvertNonLocalVariable(vars[i], cb);
                else
                    ConvertLocalVariable(vars[i], false, 0, 0);
            }
        }

        //создание нелокальной переменной
        //нелок. перем. представляется в виде нестат. поля класса-обертки над процедурой
        private void ConvertNonLocalVariable(ILocalVariableNode var, Mono.Cecil.MethodDefinition cb)
        {
            TypeInfo ti = helper.GetTypeReference(var.type);
            //cur_type сейчас хранит ссылку на созданный тип - обертку
            var fb = new Mono.Cecil.FieldDefinition(var.name, FieldAttributes.Public, ti.tp);
            cur_type.Fields.Add(fb);
            VarInfo vi = helper.AddNonLocalVariable(var, fb);
            vi.meth = smi.Peek();
            //если перем. имеет тип массив, то выделяем под него память
            //che-to nelogichno massivy v konstruktore zapisi aktivacii, a konstanty v kode procedury, nado pomenjat
            if (ti.is_arr)
            {
                if (var.inital_value == null || var.inital_value is IArrayConstantNode)
                    CreateArrayForClassField(cb.Body.GetILProcessor(), fb, ti, var.inital_value as IArrayConstantNode, var.type);
                else if (var.inital_value is IArrayInitializer)
                    CreateArrayForClassField(cb.Body.GetILProcessor(), fb, ti, var.inital_value as IArrayInitializer, var.type);
            }
            else if (var.inital_value is IArrayConstantNode)
                CreateArrayForClassField(cb.Body.GetILProcessor(), fb, ti, var.inital_value as IArrayConstantNode, var.type);
            else if (var.inital_value is IArrayInitializer)
                CreateArrayForClassField(cb.Body.GetILProcessor(), fb, ti, var.inital_value as IArrayInitializer, var.type);
            else
                if (var.type.is_value_type && var.inital_value == null || var.inital_value is IConstantNode && !(var.inital_value is INullConstantNode))
                    AddInitCall(vi, fb, ti.init_meth, var.inital_value as IConstantNode);
            in_var_init = true;
            GenerateInitCode(var, il);
            in_var_init = false;
        }

        private void ConvertLocalVariables(ILocalVariableNode[] vars)
        {
            for (int i = 0; i < vars.Length; i++)
                ConvertLocalVariable(vars[i], false, 0, 0);
        }

        //создание локальной переменной
        private void ConvertLocalVariable(IVAriableDefinitionNode var, bool add_line_info, int beg_line, int end_line)
        {
            TypeInfo ti = helper.GetTypeReference(var.type);
            bool pinned = false;
            if (ti.tp.IsPointer) pinned = true;
            var lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
            il.Body.Variables.Add(lb);
            //если модуль отладочный, задаем имя переменной
            if (save_debug_info)
                if (add_line_info)
                    il.Body.Method.DebugInformation.Scope.Variables.Add(
                        new Mono.Cecil.Cil.VariableDebugInformation(lb, var.name + ":" + beg_line + ":" + end_line)
                     );
                else
                    il.Body.Method.DebugInformation.Scope.Variables.Add(
                        new Mono.Cecil.Cil.VariableDebugInformation(lb, var.name)
                     );
            helper.AddVariable(var, lb);//добавляем переменную
            if (var.type.is_generic_parameter && var.inital_value == null)
            {
                CreateRuntimeInitCodeWithCheck(il, lb, var.type as ICommonTypeNode);
            }
            if (ti.is_arr)
            {
                if (var.inital_value == null || var.inital_value is IArrayConstantNode)
                    CreateArrayLocalVariable(il, lb, ti, var.inital_value as IArrayConstantNode, var.type);
                else if (var.inital_value is IArrayInitializer)
                    CreateArrayLocalVariable(il, lb, ti, var.inital_value as IArrayInitializer, var.type);
            }
            else if (var.inital_value is IArrayConstantNode)
                CreateArrayLocalVariable(il, lb, ti, var.inital_value as IArrayConstantNode, var.type);
            else if (var.inital_value is IArrayInitializer)
                CreateArrayLocalVariable(il, lb, ti, var.inital_value as IArrayInitializer, var.type);
            else
                if (var.type.is_value_type  && var.inital_value == null || var.inital_value is IConstantNode && !(var.inital_value is INullConstantNode))
                    AddInitCall(lb, ti.init_meth, var.inital_value as IConstantNode, var.type);
            if (ti.is_set && var.type.type_special_kind == type_special_kind.set_type && var.inital_value == null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, ti.def_cnstr);
                il.Emit(OpCodes.Stloc, lb);
            }
            in_var_init = true;
           
            if (!(var.type.is_value_type && var.inital_value is IDefaultOperatorNode))
            {
                GenerateInitCode(var, il);
            }
            in_var_init = false;
        }

        private void ConvertGlobalVariables(ICommonNamespaceVariableNode[] vars)
        {
            for (int i = 0; i < vars.Length; i++)
                ConvertGlobalVariable(vars[i]);
        }

        private void PushLdfld(Mono.Cecil.FieldDefinition fb)
        {
            if (fb.IsStatic)
            {
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldsflda, fb);
                else
                    il.Emit(OpCodes.Ldsfld, fb);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldflda, fb);
                else
                    il.Emit(OpCodes.Ldfld, fb);
            }
        }

        private void PushLdfldWithOutLdarg(Mono.Cecil.FieldDefinition fb)
        {
            if (fb.IsStatic)
            {
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldsflda, fb);
                else
                    il.Emit(OpCodes.Ldsfld, fb);
            }
            else
            {
                if (fb.FieldType.IsValueType)
                    il.Emit(OpCodes.Ldflda, fb);
                else
                    il.Emit(OpCodes.Ldfld, fb);
            }
        }

        //eto dlja inicializacii nelokalnyh peremennyh, tut nado ispolzovat disp!!!!
        private void AddInitCall(VarInfo vi, Mono.Cecil.FieldDefinition fb, Mono.Cecil.MethodReference init_meth, IConstantNode init_value)
        {
            if (init_meth != null)
            {
                if (init_value == null || init_value != null && init_value.type.type_special_kind != type_special_kind.set_type && init_value.type.type_special_kind != type_special_kind.base_set_type)
                {
                    //kladem displej tekushej procedury
                    il.Emit(OpCodes.Ldloc, vi.meth.frame);
                    PushLdfldWithOutLdarg(fb);
                    il.Emit(OpCodes.Call, init_meth);
                }
            }
            if (init_value != null)
            {
                if (init_value is IRecordConstantNode)
                {
                    var lb = new Mono.Cecil.Cil.VariableDefinition(fb.FieldType.MakePointerType());
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldloc, vi.meth.frame);
                    PushLdfldWithOutLdarg(fb);
                    il.Emit(OpCodes.Stloc, lb);
                    GenerateRecordInitCode(il, lb, init_value as IRecordConstantNode);
                }
                else
                {
                    if (!fb.IsStatic)
                    {
                        //il.Emit(OpCodes.Ldloc, vi.meth.frame);
                        //il.Emit(OpCodes.Ldfld, fb);

                        il.Emit(OpCodes.Ldloc, vi.meth.frame);
                        init_value.visit(this);
                        EmitBox(init_value, fb.FieldType);
                        il.Emit(OpCodes.Stfld, fb);
                    }
                    else
                    {
                        init_value.visit(this);
                        EmitBox(init_value, fb.FieldType);
                        il.Emit(OpCodes.Stsfld, fb);
                    }
                }
            }
        }

        private void AddInitCall(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, Mono.Cecil.MethodReference init_meth, IConstantNode init_value)
        {
            if (init_meth != null)
            {
                if (init_value == null || init_value != null && init_value.type.type_special_kind != type_special_kind.set_type && init_value.type.type_special_kind != type_special_kind.base_set_type)
                {
                    PushLdfld(fb);
                    il.Emit(OpCodes.Call, init_meth);
                }
            }
            if (init_value != null)
            {
                if (init_value is IRecordConstantNode)
                {
                    var lb = new Mono.Cecil.Cil.VariableDefinition(fb.FieldType.MakePointerType());
                    il.Body.Variables.Add(lb);
                    PushLdfld(fb);
                    il.Emit(OpCodes.Stloc, lb);
                    GenerateRecordInitCode(il, lb, init_value as IRecordConstantNode);
                }
                else
                {
                    if (!fb.IsStatic)
                    {
                        //PushLdfld(fb);
                        il.Emit(OpCodes.Ldarg_0);
                        init_value.visit(this);
                        EmitBox(init_value, fb.FieldType);
                        il.Emit(OpCodes.Stfld, fb);
                    }
                    else
                    {
                        init_value.visit(this);
                        EmitBox(init_value, fb.FieldType);
                        il.Emit(OpCodes.Stsfld, fb);
                    }
                }
            }
        }

        private void AddInitCall(Mono.Cecil.Cil.VariableDefinition lb, Mono.Cecil.MethodReference init_meth, IConstantNode init_value, ITypeNode type)
        {
            if (init_meth != null)
            {
                if (init_value == null || init_value != null && init_value.type.type_special_kind != type_special_kind.set_type && init_value.type.type_special_kind != type_special_kind.base_set_type)
                {
                    if (lb.VariableType.IsValueType)
                        il.Emit(OpCodes.Ldloca, lb);
                    else
                        il.Emit(OpCodes.Ldloc, lb);
                    il.Emit(OpCodes.Call, init_meth);
                }
            }
            if (init_value != null)
            {
                if (init_value is IRecordConstantNode)
                {
                    var llb = new Mono.Cecil.Cil.VariableDefinition(lb.VariableType.MakePointerType());
                    il.Body.Variables.Add(llb);
                    il.Emit(OpCodes.Ldloca, lb);
                    il.Emit(OpCodes.Stloc, llb);
                    GenerateRecordInitCode(il, llb, init_value as IRecordConstantNode);
                }
                else
                {
                    init_value.visit(this);
                    EmitBox(init_value, lb.VariableType);
                    il.Emit(OpCodes.Stloc, lb);
                }
            }
        }

        private void AddInitCall(Mono.Cecil.FieldDefinition fb, Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.MethodReference called_mb, Mono.Cecil.MethodReference cnstr, IConstantNode init_value)
        {
            Mono.Cecil.Cil.ILProcessor ilc = this.il;
            this.il = il;
            //il = mb.GetILGenerator();
            if (called_mb != null && (init_value == null || init_value.type.type_special_kind != type_special_kind.set_type && init_value.type.type_special_kind != type_special_kind.base_set_type))
            {
                if (fb.IsStatic == false)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    if (fb.FieldType.IsValueType)
                        il.Emit(OpCodes.Ldflda, fb);
                    else
                        il.Emit(OpCodes.Ldfld, fb);
                }
                else
                {
                    if (fb.FieldType.IsValueType)
                        il.Emit(OpCodes.Ldsflda, fb);
                    else
                        il.Emit(OpCodes.Ldsfld, fb);
                }
                il.Emit(OpCodes.Call, called_mb);
            }
            if (init_value != null)
            {
                if (init_value is IRecordConstantNode)
                {
                    var lb = new Mono.Cecil.Cil.VariableDefinition(fb.FieldType.MakePointerType());
                    il.Body.Variables.Add(lb);
                    PushLdfld(fb);
                    il.Emit(OpCodes.Stloc, lb);
                    GenerateRecordInitCode(il, lb, init_value as IRecordConstantNode);
                }
                else
                {
                    if (!(init_value is IStringConstantNode))
                    {
                        if (!fb.IsStatic)
                        {
                            il.Emit(OpCodes.Ldarg_0);
                            init_value.visit(this);
                            EmitBox(init_value, fb.FieldType);
                            il.Emit(OpCodes.Stfld, fb);
                        }
                        else
                        {
                            init_value.visit(this);
                            EmitBox(init_value, fb.FieldType);
                            il.Emit(OpCodes.Stsfld, fb);
                        }
                    }
                    else
                    {
                        if (!fb.IsStatic)
                        {
                            Instruction lbl = il.Create(OpCodes.Nop);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, fb);
                            il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.StringNullOrEmptyMethod));
                            il.Emit(OpCodes.Brfalse, lbl);
                            il.Emit(OpCodes.Ldarg_0);
                            init_value.visit(this);
                            il.Emit(OpCodes.Stfld, fb);
                            il.Append(lbl);
                        }
                        else
                        {
                            Instruction lbl = il.Create(OpCodes.Nop);
                            il.Emit(OpCodes.Ldsfld, fb);
                            il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.StringNullOrEmptyMethod));
                            il.Emit(OpCodes.Brfalse, lbl);
                            init_value.visit(this);
                            il.Emit(OpCodes.Stsfld, fb);
                            il.Append(lbl);
                        }
                    }
                }
            }
            this.il = ilc;
        }

        //(ssyy) Инициализации переменных типа параметр дженерика
        private void CreateRuntimeInitCodeWithCheck(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, ICommonTypeNode type)
        {
            if (type.runtime_initialization_marker == null) return;
            var tinfo = helper.GetTypeReference(type).tp;
            var finfo = helper.GetField(type.runtime_initialization_marker).fi;
            Instruction lab = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldsfld, finfo);
            il.Emit(OpCodes.Brfalse, lab);
            il.Emit(OpCodes.Ldsfld, finfo);
            il.Emit(OpCodes.Ldloc, lb);
            il.Emit(OpCodes.Box, tinfo);
            Mono.Cecil.MethodReference rif = null;
            if (SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info is ICompiledMethodNode)
                rif = mb.ImportReference((SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as ICompiledMethodNode).method_info);
            else
                rif = helper.GetMethod(SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as IFunctionNode).mi;
            il.Emit(OpCodes.Call, rif);
            il.Emit(OpCodes.Unbox_Any, tinfo);
            il.Emit(OpCodes.Stloc, lb);
            il.Append(lab);
        }

        //(ssyy) Инициализации полей типа параметр дженерика
        private void CreateRuntimeInitCodeWithCheck(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, ICommonTypeNode type)
        {
            if (type.runtime_initialization_marker == null) return;
            var tinfo = helper.GetTypeReference(type).tp;
            var finfo = helper.GetField(type.runtime_initialization_marker).fi;
            Instruction lab = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldsfld, finfo);
            il.Emit(OpCodes.Brfalse, lab);
            if (!fb.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
            }
            il.Emit(OpCodes.Ldsfld, finfo);
            if (fb.IsStatic)
            {
                il.Emit(OpCodes.Ldsfld, fb);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fb);
            }
            il.Emit(OpCodes.Box, tinfo);
            Mono.Cecil.MethodReference rif = null;
            if (SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info is ICompiledMethodNode)
                rif = mb.ImportReference((SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as ICompiledMethodNode).method_info);
            else
                rif = helper.GetMethod(SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as IFunctionNode).mi;
            il.Emit(OpCodes.Call, rif);
            il.Emit(OpCodes.Unbox_Any, tinfo);
            if (fb.IsStatic)
            {
                il.Emit(OpCodes.Stsfld, fb);
            }
            else
            {
                il.Emit(OpCodes.Stfld, fb);
            }
            il.Append(lab);
        }

        private void CreateArrayForClassField(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti, IArrayInitializer InitalValue, ITypeNode ArrayType)
        {
            int rank = 1;
            if (!fb.IsStatic)
                il.Emit(OpCodes.Ldarg_0);
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoundedArray(il, fb, ti);
            else
            {
                rank = get_rank(ArrayType);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(InitalValue.ElementType), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, ArrayType, helper.GetTypeReference(InitalValue.ElementType), rank, get_sizes(InitalValue, rank));

            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (!fb.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, fb);
                }
                else
                    il.Emit(OpCodes.Ldsfld, fb);
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                }
                else
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(lb);
                }
                il.Emit(OpCodes.Stloc, lb);
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue, ArrayType);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, ArrayType, rank);
            }
        }

        //поля класса
        private void CreateArrayForClassField(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti, IArrayConstantNode InitalValue, ITypeNode arr_type)
        {
            int rank = 1;
            if (!fb.IsStatic)
                il.Emit(OpCodes.Ldarg_0);
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoundedArray(il, fb, ti);
            else
            {
                rank = get_rank(arr_type);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(InitalValue.ElementType), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, arr_type, helper.GetTypeReference(arr_type.element_type), rank, get_sizes(InitalValue, rank));
            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (!fb.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, fb);
                }
                else
                    il.Emit(OpCodes.Ldsfld, fb);
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                }
                else
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(lb);
                }
                il.Emit(OpCodes.Stloc, lb);
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, arr_type, rank);
            }
        }

        private void CreateRecordLocalVariable(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, TypeInfo ti, IRecordInitializer InitalValue)
        {
            if (ti.init_meth != null)
            {
                //il.Emit(OpCodes.Ldloca, lb);
                //il.Emit(OpCodes.Call, ti.init_meth);
            }
            var llb = new Mono.Cecil.Cil.VariableDefinition(lb.VariableType.MakePointerType());
            il.Body.Variables.Add(llb);
            il.Emit(OpCodes.Ldloca, lb);
            il.Emit(OpCodes.Stloc, llb);
            GenerateRecordInitCode(il, llb, InitalValue, false);
        }

        private void CreateArrayLocalVariable(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition fb, TypeInfo ti, IArrayInitializer InitalValue, ITypeNode ArrayType)
        {
            int rank = 1;
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoudedArray(il, fb, ti);
            else
            {
                rank = get_rank(ArrayType);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(ArrayType.element_type), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, ArrayType, helper.GetTypeReference(ArrayType.element_type), rank, get_sizes(InitalValue, rank));
            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldloc, fb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                    il.Emit(OpCodes.Stloc, lb);
                }
                else
                {
                    lb = fb;
                }
                
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue, ArrayType);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, ArrayType, rank);
            }
        }

        //создание массива (точнее класса-обертки над массивом) (лок. переменная)
        private void CreateArrayLocalVariable(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition fb, TypeInfo ti, IArrayConstantNode InitalValue, ITypeNode arr_type)
        {
            int rank = 1;
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoudedArray(il, fb, ti);
            else
            {
                rank = get_rank(arr_type);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(InitalValue.ElementType), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, arr_type, helper.GetTypeReference(arr_type.element_type), rank, get_sizes(InitalValue, rank));
            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldloc, fb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                }
                else
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldloc, fb);
                }
                il.Emit(OpCodes.Stloc, lb);
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, arr_type, rank);
            }
        }

        private int get_rank(ITypeNode t)
        {
            if (t is ICommonTypeNode)
                return (t as ICommonTypeNode).rank;
            else if (t is ICompiledTypeNode)
                return (t as ICompiledTypeNode).rank;
            return 1;
        }

        private int[] get_sizes(IArrayConstantNode InitalValue, int rank)
        {
            List<int> sizes = new List<int>();
            sizes.Add(InitalValue.ElementValues.Length);
            if (rank > 1)
                sizes.AddRange(get_sizes(InitalValue.ElementValues[0] as IArrayConstantNode, rank - 1));
            return sizes.ToArray();
        }

        private int[] get_sizes(IArrayInitializer InitalValue, int rank)
        {
            List<int> sizes = new List<int>();
            sizes.Add(InitalValue.ElementValues.Length);
            if (rank > 1)
                sizes.AddRange(get_sizes(InitalValue.ElementValues[0] as IArrayInitializer, rank - 1));
            return sizes.ToArray();
        }

        private void CreateArrayGlobalVariable(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti, IArrayInitializer InitalValue, ITypeNode arr_type)
        {
            int rank = 1;
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoundedArray(il, fb, ti);
            else
            {
                rank = get_rank(arr_type);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(arr_type.element_type), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, arr_type, helper.GetTypeReference(arr_type.element_type), rank, get_sizes(InitalValue, rank));
            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldsfld, fb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                }
                else
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldsfld, fb);
                }
                il.Emit(OpCodes.Stloc, lb);
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue, arr_type);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, arr_type, rank);
            }
        }

        //глобальные переменные
        private void CreateArrayGlobalVariable(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti, IArrayConstantNode InitalValue, ITypeNode arr_type)
        {
            int rank = 1;
            if (NETGeneratorTools.IsBoundedArray(ti))
                NETGeneratorTools.CreateBoundedArray(il, fb, ti);
            else
            {
                rank = get_rank(arr_type);
                if (rank == 1)
                    CreateUnsizedArray(il, fb, helper.GetTypeReference(InitalValue.ElementType), InitalValue.ElementValues.Length);
                else
                    CreateNDimUnsizedArray(il, fb, arr_type, helper.GetTypeReference(arr_type.element_type), rank, get_sizes(InitalValue, rank));
            }
            if (InitalValue != null)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.arr_fld.FieldType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldsfld, fb);
                    il.Emit(OpCodes.Ldfld, ti.arr_fld);
                }
                else
                {
                    lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldsfld, fb);
                }
                il.Emit(OpCodes.Stloc, lb);
                if (rank == 1)
                    GenerateArrayInitCode(il, lb, InitalValue);
                else
                    GenerateNDimArrayInitCode(il, lb, InitalValue, arr_type, rank);
            }
        }

        private void CreateNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, ITypeNode arr_type, TypeInfo ti, int rank, int[] sizes)
        {
            CreateNDimUnsizedArray(il, arr_type, ti, rank, sizes, fb.FieldType.GetElementType());
            if (fb.IsStatic)
                il.Emit(OpCodes.Stsfld, fb);
            else
                il.Emit(OpCodes.Stfld, fb);
        }

        private void CreateNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition fb, ITypeNode arr_type, TypeInfo ti, int rank, int[] sizes)
        {
            CreateNDimUnsizedArray(il, arr_type, ti, rank, sizes, fb.VariableType.GetElementType());
            il.Emit(OpCodes.Stloc, fb);
        }

        private void CreateUnsizedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.FieldDefinition fb, TypeInfo ti, int size)
        {
            CreateUnsizedArray(il, ti, size, fb.FieldType.GetElementType());
            if (fb.IsStatic)
                il.Emit(OpCodes.Stsfld, fb);
            else
                il.Emit(OpCodes.Stfld, fb);
            //CreateInitCodeForUnsizedArray(il, ti, fb, size);
        }

        private void CreateUnsizedArray(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, TypeInfo ti, int size)
        {
            //il.Emit(OpCodes.Ldloca, lb);
            CreateUnsizedArray(il, ti, size, lb.VariableType.GetElementType());
            il.Emit(OpCodes.Stloc, lb);
            //CreateInitCodeForUnsizedArray(il, ti, lb, size);
        }

        private void InitializeNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, ITypeNode _arr_type, IExpressionNode[] exprs, int rank)
        {
            var arr_type = helper.GetTypeReference(_arr_type).tp.MakeArrayType(rank);
            var tmp = new Mono.Cecil.Cil.VariableDefinition(arr_type);
            il.Body.Variables.Add(tmp);
            CreateArrayLocalVariable(il, tmp, helper.GetTypeReference((exprs[2 + rank] as IArrayInitializer).type), exprs[2 + rank] as IArrayInitializer, (exprs[2 + rank] as IArrayInitializer).type);
            il.Emit(OpCodes.Ldloc, tmp);
        }

        private void InitializeUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, ITypeNode _arr_type, IExpressionNode[] exprs, int rank)
        {
            var arr_type = helper.GetTypeReference(_arr_type).tp.MakeArrayType();
            var tmp = new Mono.Cecil.Cil.VariableDefinition(arr_type);
            il.Body.Variables.Add(tmp);
            il.Emit(OpCodes.Stloc, tmp);
            for (int i = 2 + rank; i < exprs.Length; i++)
            {
                il.Emit(OpCodes.Ldloc, tmp);
                PushIntConst(il, i - 2 - rank);
                var ilb = this.il;

                if (ti != null && ti.tp.IsValueType && !TypeFactory.IsStandType(ti.tp) && (helper.IsPascalType(ti.tp) || ti.tp.HasGenericParameters || !ti.tp.Resolve().IsEnum))
                    if (!(ti.tp.IsDefinition))
                        il.Emit(OpCodes.Ldelema, ti.tp);
                if (_arr_type.is_nullable_type && exprs[i] is INullConstantNode)
                {
                    il.Emit(OpCodes.Initobj, helper.GetTypeReference(_arr_type).tp);
                    continue;
                }
                this.il = il;
                exprs[i].visit(this);
                bool box = EmitBox(exprs[i], arr_type.GetElementType());
                this.il = ilb;

                TypeInfo ti2 = helper.GetTypeReference(exprs[i].type);
                if (ti2 != null && !box)
                    NETGeneratorTools.PushStelem(il, ti2.tp);
                else
                    il.Emit(OpCodes.Stelem_Ref);

            }
            il.Emit(OpCodes.Ldloc, tmp);
        }

        private void CreateInitCodeForUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, ITypeNode _arr_type, Mono.Cecil.Cil.VariableDefinition size)
        {
            var arr_type = helper.GetTypeReference(_arr_type).tp.MakeArrayType();
            var tmp_il = this.il;
            this.il = il;
            if (ti.tp.IsValueType && ti.init_meth != null || ti.is_arr || ti.is_set || ti.is_typed_file || ti.is_text_file || ti.tp.FullName == mb.TypeSystem.String.FullName)
            {
                var tmp = new Mono.Cecil.Cil.VariableDefinition(arr_type);
                il.Body.Variables.Add(tmp);
                il.Emit(OpCodes.Stloc, tmp);
                var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                il.Body.Variables.Add(clb);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, clb);
                Instruction tlabel = il.Create(OpCodes.Nop);
                Instruction flabel = il.Create(OpCodes.Nop);
                il.Append(tlabel);
                il.Emit(OpCodes.Ldloc, clb);
                il.Emit(OpCodes.Ldloc, size);
                il.Emit(OpCodes.Bge, flabel);
                il.Emit(OpCodes.Ldloc, tmp);
                il.Emit(OpCodes.Ldloc, clb);
                if (!ti.is_arr && !ti.is_set && !ti.is_typed_file && !ti.is_text_file)
                {
                    if (ti.tp.FullName != mb.TypeSystem.String.FullName)
                    {
                        il.Emit(OpCodes.Ldelema, ti.tp);
                        il.Emit(OpCodes.Call, ti.init_meth);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Stelem_Ref);
                    }
                }
                else
                {
                    Instruction label1 = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Brfalse, label1);
                    il.Emit(OpCodes.Ldloc, tmp);
                    il.Emit(OpCodes.Ldloc, clb);
                    if (ti.is_set)
                    {
                        IConstantNode cn1 = (_arr_type as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (_arr_type as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                    }
                    else if (ti.is_typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference((_arr_type as ICommonTypeNode).element_type).tp);
                    }
                    il.Emit(OpCodes.Newobj, ti.def_cnstr);
                    il.Emit(OpCodes.Stelem_Ref);
                    il.Append(label1);
                }
                il.Emit(OpCodes.Ldloc, clb);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, clb);
                il.Emit(OpCodes.Br, tlabel);
                il.Append(flabel);
                il.Emit(OpCodes.Ldloc, tmp);
            }
            this.il = tmp_il;
        }

        struct TmpForNDimArr
        {
            public Mono.Cecil.Cil.VariableDefinition clb;
            public Instruction tlabel;
            public Instruction flabel;

            public TmpForNDimArr(Mono.Cecil.Cil.VariableDefinition clb, Instruction tlabel, Instruction flabel)
            {
                this.clb = clb;
                this.tlabel = tlabel;
                this.flabel = flabel;
            }
        }

        private void CreateInitCodeForNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, ITypeNode _arr_type, int rank, IExpressionNode[] exprs)
        {
            var arr_type = helper.GetTypeReference(_arr_type).tp.MakeArrayType(rank);
            var tmp_il = this.il;
            this.il = il;
            Mono.Cecil.MethodReference set_meth = null;
            Mono.Cecil.MethodReference addr_meth = null;
            Mono.Cecil.MethodReference get_meth = null;
            List<Mono.Cecil.TypeReference> lst2 = new List<Mono.Cecil.TypeReference>();
            for (int i = 0; i < exprs.Length; i++)
                lst2.Add(mb.TypeSystem.Int32);
            get_meth = new Mono.Cecil.MethodReference("Get", ti.tp, arr_type);
            get_meth.HasThis = true;
            foreach (var paramType in lst2)
                get_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            addr_meth =  new Mono.Cecil.MethodReference("Address", ti.tp.MakeByReferenceType(), arr_type);
            get_meth.HasThis = true;
            foreach (var paramType in lst2)
                get_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            lst2.Add(ti.tp);
            set_meth =  new Mono.Cecil.MethodReference("Set", ti.tp, mb.TypeSystem.Void);
            get_meth.HasThis = true;
            foreach (var paramType in lst2)
                get_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            if (ti.tp.IsValueType && ti.init_meth != null || ti.is_arr || ti.is_set || ti.is_typed_file || ti.is_text_file || ti.tp.FullName == mb.TypeSystem.String.FullName)
            {
                var tmp = new Mono.Cecil.Cil.VariableDefinition(arr_type);
                il.Body.Variables.Add(tmp);
                il.Emit(OpCodes.Stloc, tmp);
                List<TmpForNDimArr> lst = new List<TmpForNDimArr>();
                for (int i = 0; i < exprs.Length; i++)
                {
                    var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                    il.Body.Variables.Add(clb);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Stloc, clb);
                    Instruction tlabel = il.Create(OpCodes.Nop);
                    Instruction flabel = il.Create(OpCodes.Nop);
                    il.Append(tlabel);
                    il.Emit(OpCodes.Ldloc, clb);
                    TmpForNDimArr tmp_arr_str = new TmpForNDimArr(clb, tlabel, flabel);
                    lst.Add(tmp_arr_str);
                    exprs[i].visit(this);
                    il.Emit(OpCodes.Bge, flabel);
                }
                il.Emit(OpCodes.Ldloc, tmp);
                for (int i = 0; i < exprs.Length; i++)
                {
                    il.Emit(OpCodes.Ldloc, lst[i].clb);
                }
                if (!ti.is_arr && !ti.is_set && !ti.is_typed_file && !ti.is_text_file)
                {
                    if (ti.tp.FullName != mb.TypeSystem.String.FullName)
                    {
                        il.Emit(OpCodes.Call, addr_meth);
                        il.Emit(OpCodes.Call, ti.init_meth);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Call, set_meth);
                    }
                }
                else
                {
                    Instruction label1 = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Call, get_meth);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Brfalse, label1);
                    il.Emit(OpCodes.Ldloc, tmp);
                    for (int i = 0; i < exprs.Length; i++)
                    {
                        il.Emit(OpCodes.Ldloc, lst[i].clb);
                    }
                    if (ti.is_set)
                    {
                        IConstantNode cn1 = (_arr_type as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (_arr_type as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                    }
                    else if (ti.is_typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference((_arr_type as ICommonTypeNode).element_type).tp);
                    }
                    il.Emit(OpCodes.Newobj, ti.def_cnstr);
                    il.Emit(OpCodes.Call, set_meth);
                    il.Append(label1);
                }
                for (int i = exprs.Length - 1; i >= 0; i--)
                {
                    il.Emit(OpCodes.Ldloc, lst[i].clb);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, lst[i].clb);
                    il.Emit(OpCodes.Br, lst[i].tlabel);
                    il.Append(lst[i].flabel);
                }
                il.Emit(OpCodes.Ldloc, tmp);
            }
            this.il = tmp_il;
        }

        private void CreateInitCodeForUnsizedArray(Mono.Cecil.Cil.ILProcessor il, ITypeNode itn, IExpressionNode arr, Mono.Cecil.Cil.VariableDefinition len, Mono.Cecil.Cil.VariableDefinition start_index =null)
        {
            var tmp_il = this.il;
            TypeInfo ti = helper.GetTypeReference(itn);
            ICommonTypeNode ictn = itn as ICommonTypeNode;
            bool generic_param = (ictn != null && ictn.runtime_initialization_marker != null);
            Mono.Cecil.FieldReference finfo = null;
            Mono.Cecil.MethodReference rif = null;
            Instruction lab = default;
            this.il = il;
            if (generic_param)
            {
                finfo = helper.GetField(ictn.runtime_initialization_marker).fi;
                lab = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Ldsfld, finfo);
                il.Emit(OpCodes.Brfalse, lab);
                if (SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info is ICompiledMethodNode)
                    rif = mb.ImportReference((SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as ICompiledMethodNode).method_info);
                else
                    rif = helper.GetMethod(SystemLibrary.SystemLibInitializer.RuntimeInitializeFunction.sym_info as IFunctionNode).mi;
            }
            if (ti.tp.IsValueType && ti.init_meth != null || ti.is_arr || ti.is_set || ti.is_typed_file || ti.is_text_file || ti.tp.FullName == mb.TypeSystem.String.FullName ||
                (generic_param))
            {
                Mono.Cecil.Cil.VariableDefinition clb = null;
                if (start_index == null)
                {
                    clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                    il.Body.Variables.Add(clb);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Stloc, clb);
                }
                else
                    clb = start_index;
                
                Instruction tlabel = il.Create(OpCodes.Nop);
                Instruction flabel = il.Create(OpCodes.Nop);
                il.Append(tlabel);
                il.Emit(OpCodes.Ldloc, clb);
                il.Emit(OpCodes.Ldloc, len);
                il.Emit(OpCodes.Bge, flabel);
                if (generic_param)
                {
                    arr.visit(this);
                    il.Emit(OpCodes.Ldloc, clb);
                    il.Emit(OpCodes.Ldsfld, finfo);
                }
                arr.visit(this);
                il.Emit(OpCodes.Ldloc, clb);
                if (!ti.is_arr && !ti.is_set && !ti.is_typed_file && !ti.is_text_file)
                {
                    if (generic_param)
                    {
                        il.Emit(OpCodes.Ldelem_Any, ti.tp);
                        il.Emit(OpCodes.Box, ti.tp);
                        il.Emit(OpCodes.Call, rif);
                        il.Emit(OpCodes.Unbox_Any, ti.tp);
                        il.Emit(OpCodes.Stelem_Any, ti.tp);
                    }
                    else if (ti.tp.FullName != mb.TypeSystem.String.FullName)
                    {
                        il.Emit(OpCodes.Ldelema, ti.tp);
                        il.Emit(OpCodes.Call, ti.init_meth);
                    }
                    else
                    {
                        Instruction lb1 = il.Create(OpCodes.Nop);
                        Instruction lb2 = il.Create(OpCodes.Nop);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Beq, lb2);
                        arr.visit(this);
                        il.Emit(OpCodes.Ldloc, clb);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Brfalse, lb1);
                        il.Append(lb2);
                        arr.visit(this);
                        il.Emit(OpCodes.Ldloc, clb);
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Stelem_Ref);
                        il.Append(lb1);
                    }
                }
                else
                {
                    Instruction label1 = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Brfalse, label1);
                    arr.visit(this);
                    il.Emit(OpCodes.Ldloc, clb);
                    if (ti.is_set)
                    {
                        IConstantNode cn1 = (arr.type.element_type as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (arr.type.element_type as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                    }
                    else if (ti.is_typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference((arr.type.element_type as ICommonTypeNode).element_type).tp);
                    }

                    il.Emit(OpCodes.Newobj, ti.def_cnstr);

                    il.Emit(OpCodes.Stelem_Ref);
                    il.Append(label1);
                }
                il.Emit(OpCodes.Ldloc, clb);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, clb);
                il.Emit(OpCodes.Br, tlabel);
                il.Append(flabel);
            }
            if (generic_param)
            {
                il.Append(lab);
            }
            this.il = tmp_il;
        }

        private void CreateInitCodeForUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, IExpressionNode arr, IExpressionNode size)
        {
            var tmp_il = this.il;
            this.il = il;
            if (ti.tp.IsValueType && ti.init_meth != null || ti.is_arr || ti.is_set || ti.is_typed_file || ti.is_text_file || ti.tp.FullName == mb.TypeSystem.String.FullName)
            {
                var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                il.Body.Variables.Add(clb);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, clb);
                Instruction tlabel = il.Create(OpCodes.Nop);
                Instruction flabel = il.Create(OpCodes.Nop);
                il.Append(tlabel);
                il.Emit(OpCodes.Ldloc, clb);
                size.visit(this);
                il.Emit(OpCodes.Bge, flabel);
                arr.visit(this);
                il.Emit(OpCodes.Ldloc, clb);
                if (!ti.is_arr && !ti.is_set && !ti.is_typed_file && !ti.is_text_file)
                {
                    if (ti.tp.FullName != mb.TypeSystem.String.FullName)
                    {
                        il.Emit(OpCodes.Ldelema, ti.tp);
                        il.Emit(OpCodes.Call, ti.init_meth);
                    }
                    else
                    {
                        Instruction lb1 = il.Create(OpCodes.Nop);
                        Instruction lb2 = il.Create(OpCodes.Nop);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Beq, lb2);
                        arr.visit(this);
                        il.Emit(OpCodes.Ldloc, clb);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Brfalse, lb1);
                        il.Append(lb2);
                        arr.visit(this);
                        il.Emit(OpCodes.Ldloc, clb);
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Stelem_Ref);
                        il.Append(lb1);
                    }
                }
                else
                {
                    Instruction label1 = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Brfalse, label1);
                    arr.visit(this);
                    il.Emit(OpCodes.Ldloc, clb);
                    if (ti.is_set)
                    {
                        IConstantNode cn1 = (arr.type.element_type as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (arr.type.element_type as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                    }
                    else if (ti.is_typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference((arr.type.element_type as ICommonTypeNode).element_type).tp);
                    }

                    il.Emit(OpCodes.Newobj, ti.def_cnstr);

                    il.Emit(OpCodes.Stelem_Ref);
                    il.Append(label1);
                }
                il.Emit(OpCodes.Ldloc, clb);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, clb);
                il.Emit(OpCodes.Br, tlabel);
                il.Append(flabel);
            }
            this.il = tmp_il;
        }

        private void CreateNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, ITypeNode ArrType, TypeInfo ti, int rank, int[] sizes, Mono.Cecil.TypeReference elem_type)
        {
            var arr_type = helper.GetTypeReference(ArrType).tp;
            List<Mono.Cecil.TypeReference> types = new List<Mono.Cecil.TypeReference>();
            for (int i = 2; i < rank + 2; i++)
                types.Add(mb.TypeSystem.Int32);
            Mono.Cecil.MethodReference ci = null;
            Mono.Cecil.MethodReference mi = null;
            if (ArrType is ICompiledTypeNode)
            {
                ci = new Mono.Cecil.MethodReference(".ctor", mb.TypeSystem.Void, arr_type);
                ci.HasThis = true;

                foreach (var paramType in types)
                    ci.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            }
            else
            {
                mi = new Mono.Cecil.MethodReference(".ctor", mb.TypeSystem.Void, arr_type);
                mi.HasThis = true;

                foreach (var paramType in types)
                    mi.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            }
            for (int i = 0; i < sizes.Length; i++)
                il.Emit(OpCodes.Ldc_I4, sizes[i]);
            if (ci != null)
                il.Emit(OpCodes.Newobj, ci);
            else
                il.Emit(OpCodes.Newobj, mi);
        }

        private void CreateUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, int size, Mono.Cecil.TypeReference elem_type)
        {
            PushIntConst(il, size);
            if (ti != null)
                il.Emit(OpCodes.Newarr, ti.tp);
            else
                il.Emit(OpCodes.Newarr, elem_type);
        }

        private void CreateUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, Mono.Cecil.Cil.VariableDefinition size)
        {
            il.Emit(OpCodes.Ldloc, size);
            il.Emit(OpCodes.Newarr, ti.tp);
        }

        private void CreateNDimUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, ITypeNode tn, int rank, IExpressionNode[] sizes)
        {
            var arr_type = ti.tp.MakeArrayType(rank);
            List<Mono.Cecil.TypeReference> types = new List<Mono.Cecil.TypeReference>();
            for (int i = 2; i < rank + 2; i++)
                types.Add(mb.TypeSystem.Int32);
            Mono.Cecil.MethodReference ci = null;
            Mono.Cecil.MethodReference mi = null;
            if (tn is ICompiledTypeNode)
            {
                ci = new Mono.Cecil.MethodReference(".ctor", mb.TypeSystem.Void, arr_type);
                ci.HasThis = true;

                foreach (var paramType in types)
                    ci.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            }
            else
            {
                mi = new Mono.Cecil.MethodReference(".ctor", mb.TypeSystem.Void, arr_type);
                mi.HasThis = true;

                foreach (var paramType in types)
                    mi.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            }
            var tmp_il = this.il;
            this.il = il;
            for (int i = 2; i < rank + 2; i++)
                sizes[i].visit(this);
            this.il = tmp_il;
            if (ci != null)
                il.Emit(OpCodes.Newobj, ci);
            else
                il.Emit(OpCodes.Newobj, mi);
        }

        private void CreateUnsizedArray(Mono.Cecil.Cil.ILProcessor il, TypeInfo ti, IExpressionNode size)
        {
            size.visit(this);
            il.Emit(OpCodes.Newarr, ti.tp);
        }

        private void EmitArrayIndex(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.MethodReference set_meth, Mono.Cecil.Cil.VariableDefinition lb, IArrayConstantNode InitalValue, int rank, int act_rank, List<int> indices)
        {
            IConstantNode[] ElementValues = InitalValue.ElementValues;
            for (int i = 0; i < ElementValues.Length; i++)
            {
                if (indices.Count < act_rank)
                    indices.Add(i);
                else
                    indices[indices.Count - rank] = i;
                if (rank > 1)
                    EmitArrayIndex(il, set_meth, lb, ElementValues[i] as IArrayConstantNode, rank - 1, act_rank, indices);
                else
                {
                    if (indices.Count < act_rank)
                        indices.Add(i);
                    else
                        indices[indices.Count - rank] = i;
                    il.Emit(OpCodes.Ldloc, lb);
                    for (int j = 0; j < indices.Count; j++)
                        il.Emit(OpCodes.Ldc_I4, indices[j]);
                    var tmp_il = this.il;
                    this.il = il;
                    ElementValues[i].visit(this);
                    EmitBox(InitalValue.ElementValues[i], lb.VariableType.GetElementType());
                    this.il = tmp_il;
                    il.Emit(OpCodes.Call, set_meth);
                }
            }
        }

        private void EmitArrayIndex(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.MethodReference set_meth, Mono.Cecil.Cil.VariableDefinition lb, IArrayInitializer InitalValue, int rank, int act_rank, List<int> indices)
        {
            IExpressionNode[] ElementValues = InitalValue.ElementValues;
            for (int i = 0; i < ElementValues.Length; i++)
            {
                if (indices.Count < act_rank)
                    indices.Add(i);
                else
                    indices[indices.Count - rank] = i;
                if (rank > 1)
                    EmitArrayIndex(il, set_meth, lb, ElementValues[i] as IArrayInitializer, rank - 1, act_rank, indices);
                else
                {
                    if (indices.Count < act_rank)
                        indices.Add(i);
                    else
                        indices[indices.Count - rank] = i;
                    il.Emit(OpCodes.Ldloc, lb);
                    for (int j = 0; j < indices.Count; j++)
                        il.Emit(OpCodes.Ldc_I4, indices[j]);
                    var tmp_il = this.il;
                    this.il = il;
                    ElementValues[i].visit(this);
                    EmitBox(ElementValues[i], lb.VariableType.GetElementType());
                    this.il = tmp_il;
                    il.Emit(OpCodes.Call, set_meth);
                }
            }
        }

        private void GenerateNDimArrayInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IArrayConstantNode InitalValue, ITypeNode ArrayType, int rank)
        {
            IConstantNode[] ElementValues = InitalValue.ElementValues;
            var elem_type = helper.GetTypeReference(ArrayType.element_type).tp;
            Mono.Cecil.MethodReference set_meth = null;

            List<Mono.Cecil.TypeReference> lst = new List<Mono.Cecil.TypeReference>();
            for (int i = 0; i < rank; i++)
                lst.Add(mb.TypeSystem.Int32);
            lst.Add(elem_type);
            set_meth = new Mono.Cecil.MethodReference("Set", mb.TypeSystem.Void, lb.VariableType);
            set_meth.HasThis = true;
            foreach (var paramType in lst)
                set_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            
            List<int> indices = new List<int>();
            for (int i = 0; i < ElementValues.Length; i++)
            {
                if (i == 0)
                    indices.Add(i);
                else
                    indices[indices.Count - rank] = i;
                EmitArrayIndex(il, set_meth, lb, ElementValues[i] as IArrayConstantNode, rank - 1, rank, indices);
            }
        }

        private void GenerateNDimArrayInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IArrayInitializer InitalValue, ITypeNode ArrayType, int rank)
        {
            IExpressionNode[] ElementValues = InitalValue.ElementValues;
            var elem_type = helper.GetTypeReference(ArrayType.element_type).tp;
            Mono.Cecil.MethodReference set_meth = null;

            List<Mono.Cecil.TypeReference> lst = new List<Mono.Cecil.TypeReference>();
            for (int i = 0; i < rank; i++)
                lst.Add(mb.TypeSystem.Int32);
            lst.Add(elem_type);
            set_meth = new Mono.Cecil.MethodReference("Set", mb.TypeSystem.Void, lb.VariableType);
            set_meth.HasThis = true;
            foreach (var paramType in lst)
                set_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));

            List<int> indices = new List<int>();
            for (int i = 0; i < ElementValues.Length; i++)
            {
                if (i == 0)
                    indices.Add(i);
                else
                    indices[indices.Count - rank] = i;
                EmitArrayIndex(il, set_meth, lb, ElementValues[i] as IArrayInitializer, rank - 1, rank, indices);
            }
        }

        private void GenerateArrayInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IArrayInitializer InitalValue, ITypeNode ArrayType)
        {
            IExpressionNode[] ElementValues = InitalValue.ElementValues;
            if (ElementValues.Length > 0 && ElementValues[0] is IArrayInitializer)
            {
                bool is_unsized_array;
                Mono.Cecil.TypeReference FieldType, ArrType;
                int rank = get_rank(ElementValues[0].type);
                TypeInfo ti = helper.GetTypeReference(ElementValues[0].type);
                if (NETGeneratorTools.IsBoundedArray(ti))
                {
                    is_unsized_array = false;
                    ArrType = ti.tp;
                    FieldType = ti.arr_fld.FieldType;
                }
                else
                {
                    is_unsized_array = true;
                    ArrType = helper.GetTypeReference(ElementValues[0].type).tp;
                    FieldType = ArrType;
                }
                var llb = new Mono.Cecil.Cil.VariableDefinition(FieldType);
                il.Body.Variables.Add(llb);
                for (int i = 0; i < ElementValues.Length; i++)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    PushIntConst(il, i);
                    if (!is_unsized_array)
                    {
                        il.Emit(OpCodes.Ldelem_Any, ArrType);
                        il.Emit(OpCodes.Ldfld, ti.arr_fld);
                    }
                    else
                    {
                        if (rank > 1)
                            CreateNDimUnsizedArray(il, (ElementValues[i] as IArrayInitializer).type, helper.GetTypeReference((ElementValues[i] as IArrayInitializer).type.element_type), rank, get_sizes(ElementValues[i] as IArrayInitializer, rank), lb.VariableType.GetElementType());
                        else
                            CreateUnsizedArray(il, helper.GetTypeReference((ElementValues[i] as IArrayInitializer).type.element_type), (ElementValues[i] as IArrayInitializer).ElementValues.Length, lb.VariableType.GetElementType());
                        il.Emit(OpCodes.Stelem_Any, ArrType);
                        il.Emit(OpCodes.Ldloc, lb);
                        PushIntConst(il, i);
                        il.Emit(OpCodes.Ldelem_Any, ArrType);
                    }
                    il.Emit(OpCodes.Stloc, llb);
                    if (rank > 1)
                        GenerateNDimArrayInitCode(il, llb, ElementValues[i] as IArrayInitializer, ElementValues[i].type, rank);
                    else
                        GenerateArrayInitCode(il, llb, ElementValues[i] as IArrayInitializer, ArrayType);
                }
            }
            else
                if (ElementValues.Length > 0 && (ElementValues[0] is IRecordConstantNode || ElementValues[0] is IRecordInitializer))
            {
                TypeInfo ti = helper.GetTypeReference(ElementValues[0].type);
                var llb = new Mono.Cecil.Cil.VariableDefinition( ti.tp.MakePointerType() );
                il.Body.Variables.Add(llb);
                for (int i = 0; i < ElementValues.Length; i++)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    PushIntConst(il, i);
                    il.Emit(OpCodes.Ldelema, ti.tp);
                    il.Emit(OpCodes.Stloc, llb);
                    if (ElementValues[i] is IRecordConstantNode)
                        GenerateRecordInitCode(il, llb, ElementValues[i] as IRecordConstantNode);
                    else GenerateRecordInitCode(il, llb, ElementValues[i] as IRecordInitializer, true);
                }
            }
            else
                for (int i = 0; i < ElementValues.Length; i++)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    PushIntConst(il, i);
                    var ilb = this.il;
                    TypeInfo ti = helper.GetTypeReference(ElementValues[i].type);

                    if (ti != null && ti.is_set)
                    {
                        this.il = il;
                        IConstantNode cn1 = null;
                        IConstantNode cn2 = null;
                        if (ArrayType != null && ArrayType.element_type.element_type is ICommonTypeNode)
                        {
                            cn1 = (ArrayType.element_type.element_type as ICommonTypeNode).lower_value;
                            cn2 = (ArrayType.element_type.element_type as ICommonTypeNode).upper_value;
                        }
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                        il.Emit(OpCodes.Newobj, ti.def_cnstr);
                        il.Emit(OpCodes.Stelem_Ref);
                        il.Emit(OpCodes.Ldloc, lb);
                        PushIntConst(il, i);
                        this.il = ilb;
                    }

                    if (ti != null && ti.tp.IsValueType && !TypeFactory.IsStandType(ti.tp) && lb.VariableType.GetElementType().IsValueType && (helper.IsPascalType(ti.tp) || ti.tp.HasGenericParameters || !ti.tp.Resolve().IsEnum))
                    {
                        if (!(ti.tp.IsDefinition))
                            il.Emit(OpCodes.Ldelema, ti.tp);
                        
                    }
                    else
                        if (ti != null && ti.assign_meth != null && lb.VariableType.GetElementType().FullName != mb.TypeSystem.Object.FullName)
                        il.Emit(OpCodes.Ldelem_Ref);
                   
                    this.il = il;
                    
                    ElementValues[i].visit(this);
                    if (ti != null && ti.assign_meth != null && lb.VariableType.GetElementType().FullName != mb.TypeSystem.Object.FullName)
                    {
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        this.il = ilb;
                        continue;
                    }
                    bool box = EmitBox(ElementValues[i], lb.VariableType.GetElementType());
                    this.il = ilb;
                    if (ti != null && !box)
                        NETGeneratorTools.PushStelem(il, ti.tp);
                    else
                        il.Emit(OpCodes.Stelem_Ref);
                }
        }

        private void GenerateArrayInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IArrayConstantNode InitalValue)
        {
            IExpressionNode[] ElementValues = InitalValue.ElementValues;
            if (ElementValues[0] is IArrayConstantNode)
            {
                bool is_unsized_array;
                Mono.Cecil.TypeReference FieldType, ArrType;
                TypeInfo ti = null;
                if (NETGeneratorTools.IsBoundedArray(helper.GetTypeReference(ElementValues[0].type)))
                {
                    is_unsized_array = false;
                    ti = helper.GetTypeReference(ElementValues[0].type);
                    ArrType = ti.tp;
                    FieldType = ti.arr_fld.FieldType;
                }
                else
                {
                    is_unsized_array = true;
                    ArrType = helper.GetTypeReference(ElementValues[0].type).tp;
                    FieldType = ArrType;
                }
                var llb = new Mono.Cecil.Cil.VariableDefinition(FieldType);
                il.Body.Variables.Add(llb);
                for (int i = 0; i < ElementValues.Length; i++)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    PushIntConst(il, i);
                    if (!is_unsized_array)
                    {
                        il.Emit(OpCodes.Ldelem_Any, ArrType);
                        il.Emit(OpCodes.Ldfld, ti.arr_fld);
                    }
                    else
                    {
                        CreateUnsizedArray(il, helper.GetTypeReference((ElementValues[i] as IArrayConstantNode).type.element_type), (ElementValues[i] as IArrayConstantNode).ElementValues.Length, lb.VariableType.GetElementType());
                        il.Emit(OpCodes.Stelem_Any, ArrType);
                        il.Emit(OpCodes.Ldloc, lb);
                        PushIntConst(il, i);
                        il.Emit(OpCodes.Ldelem_Any, ArrType);
                    }
                    il.Emit(OpCodes.Stloc, llb);
                    GenerateArrayInitCode(il, llb, ElementValues[i] as IArrayConstantNode);
                }
            }
            else
                if (ElementValues[0] is IRecordConstantNode)
                {
                    TypeInfo ti = helper.GetTypeReference(ElementValues[0].type);
                    var llb = new Mono.Cecil.Cil.VariableDefinition(ti.tp.MakePointerType());
                    il.Body.Variables.Add(llb);
                    for (int i = 0; i < ElementValues.Length; i++)
                    {
                        il.Emit(OpCodes.Ldloc, lb);
                        PushIntConst(il, i);
                        il.Emit(OpCodes.Ldelema, ti.tp);
                        il.Emit(OpCodes.Stloc, llb);
                        GenerateRecordInitCode(il, llb, ElementValues[i] as IRecordConstantNode);
                    }
                }
                else
                    for (int i = 0; i < ElementValues.Length; i++)
                    {
                        il.Emit(OpCodes.Ldloc, lb);
                        TypeInfo ti = helper.GetTypeReference(ElementValues[i].type);
                        PushIntConst(il, i);
                        if (ti != null && ti.tp.IsValueType && !TypeFactory.IsStandType(ti.tp) && !TypeIsEnum(ti.tp))
                            il.Emit(OpCodes.Ldelema, ti.tp);
                        Mono.Cecil.Cil.ILProcessor ilb = this.il;
                        this.il = il;
                        ElementValues[i].visit(this);
                        this.il = ilb;
                        bool box = EmitBox(ElementValues[i], lb.VariableType.GetElementType());
                        this.il = ilb;
                        if (ti != null && !box)
                            NETGeneratorTools.PushStelem(il, ti.tp);
                        else
                            il.Emit(OpCodes.Stelem_Ref);
                    }
        }

        private void GenerateRecordInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IRecordInitializer init_value, bool is_in_arr)
        {
            ICommonTypeNode ctn = init_value.type as ICommonTypeNode;
            IExpressionNode[] FieldValues = init_value.FieldValues;
            List<ICommonClassFieldNode> Fields = new List<ICommonClassFieldNode>();
            foreach (var field in ctn.fields)
                if (field.polymorphic_state != polymorphic_state.ps_static)
                    Fields.Add(field);

            for (int i = 0; i < Fields.Count; i++)
            {
                FldInfo field = helper.GetField(Fields[i]);
                if (FieldValues[i] is IArrayInitializer)
                {
                    TypeInfo ti = helper.GetTypeReference(FieldValues[i].type);
                    var alb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(alb);
                    CreateArrayLocalVariable(il, alb, ti, FieldValues[i] as IArrayInitializer, FieldValues[i].type);
                    il.Emit(OpCodes.Ldloc, lb);
                    il.Emit(OpCodes.Ldloc, alb);
                    il.Emit(OpCodes.Stfld, field.fi);
                }
                else
                    if (FieldValues[i] is IRecordInitializer)
                    {
                        var llb = new Mono.Cecil.Cil.VariableDefinition(field.fi.FieldType.MakePointerType());
                        il.Body.Variables.Add(llb);
                        il.Emit(OpCodes.Ldloc, lb);
                        il.Emit(OpCodes.Ldflda, field.fi);
                        il.Emit(OpCodes.Stloc, llb);
                        GenerateRecordInitCode(il, llb, FieldValues[i] as IRecordInitializer, false);
                    }
                    else
                    {
                        is_dot_expr = false;
                        if (is_in_arr)
                            il.Emit(OpCodes.Ldloc, lb);
                        else
                            il.Emit(OpCodes.Ldloc, lb);
                        var tmp_il = this.il;
                        this.il = il;
                        FieldValues[i].visit(this);
                        this.il = tmp_il;
                        il.Emit(OpCodes.Stfld, field.fi);
                    }
            }
        }

        private void GenerateRecordInitCode(Mono.Cecil.Cil.ILProcessor il, Mono.Cecil.Cil.VariableDefinition lb, IRecordConstantNode init_value)
        {
            ICommonTypeNode ctn = init_value.type as ICommonTypeNode;
            IConstantNode[] FieldValues = init_value.FieldValues;
            List<ICommonClassFieldNode> Fields = new List<ICommonClassFieldNode>();
            foreach (var field in ctn.fields)
                if (field.polymorphic_state != polymorphic_state.ps_static)
                    Fields.Add(field);

            for (int i = 0; i < Fields.Count; i++)
            {
                FldInfo field = helper.GetField(Fields[i]);
                if (FieldValues[i] is IArrayConstantNode)
                {
                    TypeInfo ti = helper.GetTypeReference(FieldValues[i].type);
                    var alb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                    il.Body.Variables.Add(alb);
                    CreateArrayLocalVariable(il, alb, ti, FieldValues[i] as IArrayConstantNode, FieldValues[i].type);
                    il.Emit(OpCodes.Ldloc, lb);
                    il.Emit(OpCodes.Ldloc, alb);
                    il.Emit(OpCodes.Stfld, field.fi);
                }
                else
                    if (FieldValues[i] is IRecordConstantNode)
                    {
                        var llb = new Mono.Cecil.Cil.VariableDefinition(field.fi.FieldType.MakePointerType());
                        il.Body.Variables.Add(llb);
                        il.Emit(OpCodes.Ldloc, lb);
                        il.Emit(OpCodes.Ldflda, field.fi);
                        il.Emit(OpCodes.Stloc, llb);
                        GenerateRecordInitCode(il, llb, FieldValues[i] as IRecordConstantNode);
                    }
                    else
                    {
                        //bool tmp = is_dot_expr;

                        is_dot_expr = false;
                        il.Emit(OpCodes.Ldloc, lb);
                        var tmp_il = this.il;
                        this.il = il;
                        FieldValues[i].visit(this);
                        this.il = tmp_il;
                        il.Emit(OpCodes.Stfld, field.fi);
                        //is_dot_expr = tmp;
                    }
            }
        }

        private bool in_var_init = false;

        private Mono.Cecil.TypeReference get_type_reference_for_pascal_attributes(ITypeNode tn)
        {
            if (tn.type_special_kind == type_special_kind.short_string)
            {
                return CreateShortStringType(tn);
            }
            else if (tn.type_special_kind == type_special_kind.typed_file)
            {
                return CreateTypedFileType(tn as ICommonTypeNode);
            }
            else if (tn.type_special_kind == type_special_kind.set_type)
            {
                return CreateTypedSetType(tn as ICommonTypeNode);
            }
            else
                return helper.GetTypeReference(tn).tp;
        }

        private void add_possible_type_attribute(Mono.Cecil.TypeDefinition tb, ITypeSynonym type)
        {
            var orig_type = get_type_reference_for_pascal_attributes(type.original_type);
            CustomAttributeBuilder cust_bldr = null;
            if (type.original_type is ICompiledTypeNode || type.original_type is IRefTypeNode && (type.original_type as IRefTypeNode).pointed_type is ICompiledTypeNode)
                cust_bldr = new CustomAttributeBuilder(this.TypeSynonimAttributeConstructor).AddConstructorArgs(new object[1] { orig_type });
            else
                cust_bldr = new CustomAttributeBuilder(this.TypeSynonimAttributeConstructor).AddConstructorArgs(new object[1] { orig_type.FullName });
            tb.CustomAttributes.Add(cust_bldr.Build());
        }

        private void add_possible_type_attribute(Mono.Cecil.TypeDefinition tb, ITypeNode type)
        {
            if (comp_opt.target != TargetType.Dll)
                return;
            if (type.type_special_kind == type_special_kind.typed_file)
            {
                var elem_type = helper.GetTypeReference(type.element_type).tp;
                CustomAttributeBuilder cust_bldr = null;
                if (type.element_type is ICompiledTypeNode || type.element_type is IRefTypeNode && (type.element_type as IRefTypeNode).pointed_type is ICompiledTypeNode)
                    cust_bldr = new CustomAttributeBuilder(this.FileOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type });
                else
                    cust_bldr = new CustomAttributeBuilder(this.FileOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type.FullName });
                tb.CustomAttributes.Add(cust_bldr.Build());
            }
            else if (type.type_special_kind == type_special_kind.set_type)
            {
                var elem_type = helper.GetTypeReference(type.element_type).tp;
                CustomAttributeBuilder cust_bldr = null;
                if (type.element_type is ICompiledTypeNode || type.element_type is IRefTypeNode && (type.element_type as IRefTypeNode).pointed_type is ICompiledTypeNode)
                    cust_bldr = new CustomAttributeBuilder(this.SetOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type });
                else
                    cust_bldr = new CustomAttributeBuilder(this.SetOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type.FullName });
                tb.CustomAttributes.Add(cust_bldr.Build());
            }
            else if (type.type_special_kind == type_special_kind.short_string)
            {
                int len = (type as IShortStringTypeNode).Length;
                Mono.Cecil.CustomAttribute cust_bldr = new Mono.Cecil.CustomAttribute(this.ShortStringAttributeConstructor);
                cust_bldr.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(mb.TypeSystem.Int32, len));
                tb.CustomAttributes.Add(cust_bldr);
            }
        }

        private void add_possible_type_attribute(Mono.Cecil.FieldDefinition fb, ITypeNode type)
        {
            if (comp_opt.target != TargetType.Dll)
                return;
            if (type.type_special_kind == type_special_kind.typed_file)
            {
                var elem_type = helper.GetTypeReference(type.element_type).tp;
                CustomAttributeBuilder cust_bldr = null;
                if (type.element_type is ICompiledTypeNode || type.element_type is IRefTypeNode && (type.element_type as IRefTypeNode).pointed_type is ICompiledTypeNode)
                    cust_bldr = new CustomAttributeBuilder(this.FileOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type });
                else
                    cust_bldr = new CustomAttributeBuilder(this.FileOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type.FullName });
                fb.CustomAttributes.Add(cust_bldr.Build());
            }
            else if (type.type_special_kind == type_special_kind.set_type)
            {
                var elem_type = helper.GetTypeReference(type.element_type).tp;
                CustomAttributeBuilder cust_bldr = null;
                if (type.element_type is ICompiledTypeNode || type.element_type is IRefTypeNode && (type.element_type as IRefTypeNode).pointed_type is ICompiledTypeNode)
                    cust_bldr = new CustomAttributeBuilder(this.SetOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type });
                else
                    cust_bldr = new CustomAttributeBuilder(this.SetOfAttributeConstructor).AddConstructorArgs(new object[1] { elem_type.FullName });
                fb.CustomAttributes.Add(cust_bldr.Build());
            }
            else if (type.type_special_kind == type_special_kind.short_string)
            {
                int len = (type as IShortStringTypeNode).Length;
                CustomAttributeBuilder cust_bldr = new CustomAttributeBuilder(this.ShortStringAttributeConstructor).AddConstructorArgs(new object[] {len});
                fb.CustomAttributes.Add(cust_bldr.Build());
            }
        }

        //перевод глобальной переменной (переменной модуля и основной программы)
        private void ConvertGlobalVariable(ICommonNamespaceVariableNode var)
        {
            //Console.WriteLine(is_in_unit);
            //if (is_in_unit && helper.IsUsed(var)==false) return;
            if (helper.GetVariable(var) != null)
                return;
            TypeInfo ti = helper.GetTypeReference(var.type);
            var fb = new Mono.Cecil.FieldDefinition(var.name, FieldAttributes.Public | FieldAttributes.Static, ti.tp);
            cur_type.Fields.Add(fb);
            helper.AddGlobalVariable(var, fb);
            add_possible_type_attribute(fb, var.type);
            
            //если переменная имеет тип - массив, то создаем его
            if (ti.is_arr)
            {
                if (var.inital_value == null || var.inital_value is IArrayConstantNode)
                    CreateArrayGlobalVariable(il, fb, ti, var.inital_value as IArrayConstantNode, var.type);
                else if (var.inital_value is IArrayInitializer)
                    CreateArrayGlobalVariable(il, fb, ti, var.inital_value as IArrayInitializer, var.type);
            }
            else if (var.inital_value is IArrayConstantNode)
                CreateArrayGlobalVariable(il, fb, ti, var.inital_value as IArrayConstantNode, var.type);
            else if (var.inital_value is IArrayInitializer)
                CreateArrayGlobalVariable(il, fb, ti, var.inital_value as IArrayInitializer, var.type);
            else
                if (var.type.is_value_type  && var.inital_value == null || var.inital_value is IConstantNode && !(var.inital_value is INullConstantNode))
                    AddInitCall(il, fb, ti.init_meth, var.inital_value as IConstantNode);
            if (ti.is_set && var.type.type_special_kind == type_special_kind.set_type && var.inital_value == null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, ti.def_cnstr);
                il.Emit(OpCodes.Stsfld, fb);
            }
            in_var_init = true;
            GenerateInitCode(var, il);
            in_var_init = false;
        }

        //private void GenerateInit

        private void GenerateInitCode(IVAriableDefinitionNode var, Mono.Cecil.Cil.ILProcessor ilg)
        {
            var ilgn = il;
            IExpressionNode expr = var.inital_value;
            il = ilg;
            if (expr != null && save_debug_info && comp_opt.GenerateDebugInfoForInitalValue)
            {
                if (expr.Location != null && !(expr is IConstantNode) && !(expr is IArrayInitializer) && !(expr is IRecordInitializer))
                    MarkSequencePoint(expr.Location);
            }
            if (expr != null && !(expr is IConstantNode) && !(expr is IArrayInitializer))
            {
                expr.visit(this);
                if (expr.type != null && (!(expr is IBasicFunctionCallNode) && expr is IFunctionCallNode fcn && (fcn.function.name == "op_Assign" || fcn.function.name == ":=")))
                    il.Emit(OpCodes.Pop);
            }
            il = ilgn;
        }

        public override void visit(SemanticTree.ICompiledPropertyNode value)
        {

        }

        public override void visit(SemanticTree.IBasicPropertyNode value)
        {

        }

        private bool is_get_set = false;
        private string cur_prop_name;

        //перевод свойства класса
        public override void visit(SemanticTree.ICommonPropertyNode value)
        {
            //получаем тип свойства
            var ret_type = helper.GetTypeReference(value.property_type).tp;
            //получаем параметры свойства
            Mono.Cecil.TypeReference[] tt = GetParamTypes(value);
            Mono.Cecil.PropertyAttributes pa = Mono.Cecil.PropertyAttributes.None;
            if (value.common_comprehensive_type.default_property == value)
                pa = Mono.Cecil.PropertyAttributes.HasDefault;

            var pb = new Mono.Cecil.PropertyDefinition(value.name, pa, ret_type);
            cur_type.Properties.Add(pb);
            
            foreach (var param in tt)
                pb.Parameters.Add(new Mono.Cecil.ParameterDefinition(param));
            
            helper.AddProperty(value, pb);
            //переводим заголовки методов доступа
            if (value.get_function != null)
            {
                is_get_set = true; cur_prop_name = "get_" + value.name;
                ConvertMethodHeader((ICommonMethodNode)value.get_function);
                is_get_set = false;
                var mb = helper.GetMethodBuilder(value.get_function);
                TypeInfo ti = helper.GetTypeReference(value.comperehensive_type);
                if (ti.is_arr) AddToCompilerGenerated(mb);
            }
            if (value.set_function != null)
            {
                is_get_set = true; cur_prop_name = "set_" + value.name;
                ConvertMethodHeader((ICommonMethodNode)value.set_function);
                is_get_set = false;
            }
            //привязываем эти методы к свойству
            if (value.get_function != null)
                pb.GetMethod = helper.GetMethodBuilder(value.get_function);
            if (value.set_function != null)
                pb.SetMethod = helper.GetMethodBuilder(value.set_function);
            MakeAttribute(value);
        }

        public override void visit(SemanticTree.IPropertyNode value)
        {

        }

        public override void visit(SemanticTree.IConstantDefinitionNode value)
        {

        }

        public override void visit(SemanticTree.ICompiledParameterNode value)
        {

        }

        public override void visit(SemanticTree.IBasicParameterNode value)
        {

        }

        public override void visit(SemanticTree.ICommonParameterNode value)
        {

        }

        public override void visit(SemanticTree.IParameterNode value)
        {

        }

        public override void visit(SemanticTree.ICompiledClassFieldNode value)
        {

        }

        //добавление методов копирования и проч. в массив
        private void AddSpecialMembersToArray(SemanticTree.ICommonClassFieldNode value, FieldAttributes fattr)
        {
            TypeInfo aux_ti = helper.GetTypeReference(value.comperehensive_type);
            if (aux_ti.clone_meth != null) return;
            aux_ti.is_arr = true;
            ISimpleArrayNode arr_type = value.type as ISimpleArrayNode;
            TypeInfo elem_ti = helper.GetTypeReference(arr_type.element_type);
            //переводим ISimpleArrayNode в .NET тип
            var type = elem_ti.tp.MakeArrayType();
            //определяем поле для хранения ссылки на массив .NET
            var fb = new Mono.Cecil.FieldDefinition(value.name, fattr, type);
            cur_type.Fields.Add(fb);
            helper.AddField(value, fb);
            var tb = (Mono.Cecil.TypeDefinition)aux_ti.tp;
            //определяем конструктор, в котором создаем массив
            var cb = new Mono.Cecil.MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
            tb.Methods.Add(cb);
            TypeInfo ti = helper.AddType(value.type, tb);
            ti.tp = type;
            ti.arr_fld = fb;
            aux_ti.def_cnstr = cb;
            aux_ti.arr_fld = fb;
            aux_ti.arr_len = arr_type.length;
            var cb_il = cb.Body.GetILProcessor();
            //вызов констуктора по умолчанию
            cb_il.Emit(OpCodes.Ldarg_0);
            cb_il.Emit(OpCodes.Call, mb.TypeSystem.Object.Resolve().Methods.Single(item => item.Name == ".ctor"));

            //создание массива
            if (!elem_ti.is_arr)
            {
                //если массив одномерный
                cb_il.Emit(OpCodes.Ldarg_0);
                cb_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                cb_il.Emit(OpCodes.Newarr, elem_ti.tp);
                cb_il.Emit(OpCodes.Stfld, fb);
                //вызовы методов $Init$ для каждого элемента массива
                if (elem_ti.is_set || elem_ti.is_typed_file || elem_ti.is_text_file || elem_ti.tp.FullName == mb.TypeSystem.String.FullName || elem_ti.tp.IsValueType && elem_ti.init_meth != null)
                {
                    //cb_il.Emit(OpCodes.Ldarg_0);
                    var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                    cb_il.Body.Variables.Add(clb);
                    cb_il.Emit(OpCodes.Ldc_I4_0);
                    cb_il.Emit(OpCodes.Stloc, clb);
                    Instruction tlabel = cb_il.Create(OpCodes.Nop);
                    Instruction flabel = cb_il.Create(OpCodes.Nop);
                    cb_il.Append(tlabel);
                    cb_il.Emit(OpCodes.Ldloc, clb);
                    cb_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                    cb_il.Emit(OpCodes.Bge, flabel);
                    cb_il.Emit(OpCodes.Ldarg_0);
                    cb_il.Emit(OpCodes.Ldfld, fb);
                    cb_il.Emit(OpCodes.Ldloc, clb);
                    if (!elem_ti.is_set && !elem_ti.is_typed_file && !elem_ti.is_text_file)
                    {
                        if (elem_ti.tp.FullName != mb.TypeSystem.String.FullName)
                        {
                            cb_il.Emit(OpCodes.Ldelema, elem_ti.tp);
                            cb_il.Emit(OpCodes.Call, elem_ti.init_meth);
                        }
                        else
                        {
                            cb_il.Emit(OpCodes.Ldstr, "");
                            cb_il.Emit(OpCodes.Stelem_Ref);
                        }
                    }
                    else if (elem_ti.is_set)
                    {
                        IConstantNode cn1 = (arr_type.element_type as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (arr_type.element_type as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            var tmp_il = il;
                            il = cb_il;
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                            il = tmp_il;
                        }
                        else
                        {
                            cb_il.Emit(OpCodes.Ldnull);
                            cb_il.Emit(OpCodes.Ldnull);
                        }
                        cb_il.Emit(OpCodes.Newobj, elem_ti.def_cnstr);
                        cb_il.Emit(OpCodes.Stelem_Ref);
                    }
                    else if (elem_ti.is_typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(cb_il, helper.GetTypeReference(arr_type.element_type.element_type).tp);
                        cb_il.Emit(OpCodes.Newobj, elem_ti.def_cnstr);
                        cb_il.Emit(OpCodes.Stelem_Ref);
                    }
                    else if (elem_ti.is_text_file)
                    {
                        cb_il.Emit(OpCodes.Newobj, elem_ti.def_cnstr);
                        cb_il.Emit(OpCodes.Stelem_Ref);
                    }
                    cb_il.Emit(OpCodes.Ldloc, clb);
                    cb_il.Emit(OpCodes.Ldc_I4_1);
                    cb_il.Emit(OpCodes.Add);
                    cb_il.Emit(OpCodes.Stloc, clb);
                    cb_il.Emit(OpCodes.Br, tlabel);
                    cb_il.Append(flabel);
                }
                cb_il.Emit(OpCodes.Ret);
            }
            else
            {
                //если массив многомерный, то в цикле по создаем
                //элементы-массивы
                var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                cb_il.Body.Variables.Add(clb);
                cb_il.Emit(OpCodes.Ldc_I4_0);
                cb_il.Emit(OpCodes.Stloc, clb);
                cb_il.Emit(OpCodes.Ldarg_0);
                cb_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                cb_il.Emit(OpCodes.Newarr, elem_ti.tp);
                cb_il.Emit(OpCodes.Stfld, fb);
                Instruction tlabel = cb_il.Create(OpCodes.Nop);
                Instruction flabel = cb_il.Create(OpCodes.Nop);
                cb_il.Append(tlabel);
                cb_il.Emit(OpCodes.Ldloc, clb);
                cb_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                cb_il.Emit(OpCodes.Bge, flabel);
                cb_il.Emit(OpCodes.Ldarg_0);
                cb_il.Emit(OpCodes.Ldfld, fb);
                cb_il.Emit(OpCodes.Ldloc, clb);
                cb_il.Emit(OpCodes.Newobj, elem_ti.def_cnstr);
                cb_il.Emit(OpCodes.Stelem_Ref);
                cb_il.Emit(OpCodes.Ldloc, clb);
                cb_il.Emit(OpCodes.Ldc_I4_1);
                cb_il.Emit(OpCodes.Add);
                cb_il.Emit(OpCodes.Stloc, clb);
                cb_il.Emit(OpCodes.Br, tlabel);
                cb_il.Append(flabel);
                cb_il.Emit(OpCodes.Ret);
            }
            //определяем метод копирование массива
            var clone_mb = new Mono.Cecil.MethodDefinition("Clone", MethodAttributes.Public, tb);
            tb.Methods.Add(clone_mb);
            var clone_il = clone_mb.Body.GetILProcessor();
            //если массив одномерный
            if (elem_ti.clone_meth == null)
            {
                MarkSequencePoint(clone_il, 0xFFFFFF, 0, 0xFFFFFF, 0);
                var lb = new Mono.Cecil.Cil.VariableDefinition(tb);
                clone_il.Body.Variables.Add(lb);
                clone_il.Emit(OpCodes.Newobj, cb);
                clone_il.Emit(OpCodes.Stloc, lb);

                clone_il.Emit(OpCodes.Ldarg_0);
                clone_il.Emit(OpCodes.Ldfld, fb);
                clone_il.Emit(OpCodes.Ldloc, lb);
                clone_il.Emit(OpCodes.Ldfld, fb);
                clone_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                clone_il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ArrayCopyMethod));

                clone_il.Emit(OpCodes.Ldloc, lb);
                clone_il.Emit(OpCodes.Ret);
            }
            else
            {
                MarkSequencePoint(clone_il, 0xFFFFFF, 0, 0xFFFFFF, 0);
                //если массив многомерный, то в цикле копируем
                var lb = new Mono.Cecil.Cil.VariableDefinition(tb);
                clone_il.Body.Variables.Add(lb);
                clone_il.Emit(OpCodes.Newobj, cb);
                clone_il.Emit(OpCodes.Stloc, lb);

                var clb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                clone_il.Body.Variables.Add(clb);
                clone_il.Emit(OpCodes.Ldc_I4_0);
                clone_il.Emit(OpCodes.Stloc, clb);
                Instruction tlabel = clone_il.Create(OpCodes.Nop);
                Instruction flabel = clone_il.Create(OpCodes.Nop);
                clone_il.Append(tlabel);
                clone_il.Emit(OpCodes.Ldloc, clb);
                clone_il.Emit(OpCodes.Ldc_I4, arr_type.length);
                clone_il.Emit(OpCodes.Bge, flabel);
                clone_il.Emit(OpCodes.Ldloc, lb);
                clone_il.Emit(OpCodes.Ldfld, fb);
                clone_il.Emit(OpCodes.Ldloc, clb);
                if (elem_ti.tp.IsValueType)
                    clone_il.Emit(OpCodes.Ldelema, elem_ti.tp);
                clone_il.Emit(OpCodes.Ldarg_0);
                clone_il.Emit(OpCodes.Ldfld, fb);
                clone_il.Emit(OpCodes.Ldloc, clb);
                if (!elem_ti.tp.IsValueType)
                    clone_il.Emit(OpCodes.Ldelem_Ref);
                else
                    clone_il.Emit(OpCodes.Ldelema, elem_ti.tp);
                clone_il.Emit(OpCodes.Call, elem_ti.clone_meth);
                if (!elem_ti.tp.IsValueType)
                    clone_il.Emit(OpCodes.Stelem_Ref);
                else
                    clone_il.Emit(OpCodes.Stobj, elem_ti.tp);
                clone_il.Emit(OpCodes.Ldloc, clb);
                clone_il.Emit(OpCodes.Ldc_I4_1);
                clone_il.Emit(OpCodes.Add);
                clone_il.Emit(OpCodes.Stloc, clb);
                clone_il.Emit(OpCodes.Br, tlabel);
                clone_il.Append(flabel);
                clone_il.Emit(OpCodes.Ldloc, lb);
                clone_il.Emit(OpCodes.Ret);
            }
            //привязываем метод копирования
            //он нужен будет при передаче массива в кач-ве параметра по значению
            aux_ti.clone_meth = clone_mb;
        }

        private FieldAttributes ConvertFALToFieldAttributes(field_access_level fal)
        {
            switch (fal)
            {
                case field_access_level.fal_public: return FieldAttributes.Public;
                case field_access_level.fal_internal: return FieldAttributes.Assembly;
                case field_access_level.fal_protected: return FieldAttributes.FamORAssem;
                case field_access_level.fal_private: return FieldAttributes.Assembly;
            }
            return FieldAttributes.Assembly;
        }

        private FieldAttributes AddPSToFieldAttributes(polymorphic_state ps, FieldAttributes fa)
        {
            switch (ps)
            {
                case polymorphic_state.ps_static: fa |= FieldAttributes.Static; break;
            }
            return fa;
        }

        private MethodAttributes ConvertFALToMethodAttributes(field_access_level fal)
        {
            switch (fal)
            {
                case field_access_level.fal_public: return MethodAttributes.Public;
                case field_access_level.fal_internal: return (comp_opt.target == TargetType.Dll && pabc_rtl_converted) ? MethodAttributes.Public : MethodAttributes.Assembly;
                case field_access_level.fal_protected: return MethodAttributes.FamORAssem;
                case field_access_level.fal_private: return MethodAttributes.Assembly;
            }
            return MethodAttributes.Assembly;
        }


        //перевод поля класса
        public override void visit(SemanticTree.ICommonClassFieldNode value)
        {
            //if (is_in_unit && helper.IsUsed(value)==false) return;
            FieldAttributes fattr = ConvertFALToFieldAttributes(value.field_access_level);
            fattr = AddPSToFieldAttributes(value.polymorphic_state, fattr);
            Mono.Cecil.TypeReference type = null;
            TypeInfo ti = helper.GetTypeReference(value.type);
            //далее идет байда, связанная с массивами, мы здесь добавляем методы копирования и проч.
            if (ti == null)
            {
                AddSpecialMembersToArray(value, fattr);
            }
            else
            {
                //иначе все хорошо
                type = ti.tp;
                var fb = new Mono.Cecil.FieldDefinition(value.name, fattr, type);
                cur_type.Fields.Add(fb);
                helper.AddField(value, fb);
                MakeAttribute(value);
                if (cur_type.IsValueType && cur_ti.clone_meth != null)
                {
                    NETGeneratorTools.CloneField(cur_ti.clone_meth as Mono.Cecil.MethodDefinition, fb, ti);
                    NETGeneratorTools.AssignField(cur_ti.assign_meth as Mono.Cecil.MethodDefinition, fb, ti);
                    switch (value.type.type_special_kind)
                    {
                        case type_special_kind.array_wrapper:
                        case type_special_kind.set_type:
                        case type_special_kind.short_string:
                        case type_special_kind.typed_file:
                            NETGeneratorTools.FixField(cur_ti.fix_meth, fb, ti);
                            break;
                    }
                }
            }
        }

        internal void GenerateInitCodeForFields(SemanticTree.ICommonTypeNode ctn)
        {
            TypeInfo ti = helper.GetTypeReference(ctn);
            //(ssyy) 16.05.08 добавил проверку, это надо для ф-ций, зависящих от generic-параметров.
            if (ti == null) return;
            if (!ctn.IsInterface && ti.init_meth != null)
            {
                foreach (SemanticTree.ICommonClassFieldNode ccf in ctn.fields)
                    if (ccf.polymorphic_state != polymorphic_state.ps_static)
                        GenerateInitCodeForField(ccf);
                    else
                        GenerateInitCodeForStaticField(ccf);
                foreach (IClassConstantDefinitionNode cnst in ctn.constants)
                    if (cnst.constant_value is IArrayConstantNode)
                    {
                        GenerateInitCodeForClassConstant(cnst);
                    }
            }
        }

        internal void GenerateRetForInitMeth(SemanticTree.ICommonTypeNode ctn)
        {
            TypeInfo ti = helper.GetTypeReference(ctn);
            if (ti == null)
            {
                return;
            }
            if (!ctn.IsInterface && ti.init_meth != null)
                (ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        internal void GenerateInitCodeForStaticField(SemanticTree.ICommonClassFieldNode value)
        {
            TypeInfo ti = helper.GetTypeReference(value.type), cur_ti = helper.GetTypeReference(value.comperehensive_type);
            var fb = helper.GetField(value).fi as Mono.Cecil.FieldDefinition;
            if (value.type.is_generic_parameter && value.inital_value == null)
            {
                CreateRuntimeInitCodeWithCheck((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, value.type as ICommonTypeNode);
            }
            if (ti.is_arr)
            {
                if (value.inital_value == null || value.inital_value is IArrayConstantNode)
                    CreateArrayForClassField(cur_ti.static_cnstr.Body.GetILProcessor(), fb, ti, value.inital_value as IArrayConstantNode, value.type);
                else if (value.inital_value is IArrayInitializer)
                    CreateArrayForClassField(cur_ti.static_cnstr.Body.GetILProcessor(), fb, ti, value.inital_value as IArrayInitializer, value.type);
            }
            else if (value.inital_value is IArrayConstantNode)
                CreateArrayForClassField(cur_ti.static_cnstr.Body.GetILProcessor(), fb, ti, value.inital_value as IArrayConstantNode, value.type);
            else if (value.inital_value is IArrayInitializer)
                CreateArrayForClassField(cur_ti.static_cnstr.Body.GetILProcessor(), fb, ti, value.inital_value as IArrayInitializer, value.type);
            else
                if (value.type.is_value_type  && value.inital_value == null || value.inital_value is IConstantNode && !(value.inital_value is INullConstantNode))
                    AddInitCall(fb, cur_ti.static_cnstr.Body.GetILProcessor(), ti.init_meth, ti.def_cnstr, value.inital_value as IConstantNode);
            in_var_init = true;
            GenerateInitCode(value, cur_ti.static_cnstr.Body.GetILProcessor());
            in_var_init = false;
        }

        internal void GenerateInitCodeForClassConstant(SemanticTree.IClassConstantDefinitionNode value)
        {
            TypeInfo ti = helper.GetTypeReference(value.type), cur_ti = helper.GetTypeReference(value.comperehensive_type);
            var fb = helper.GetConstant(value).fb as Mono.Cecil.FieldDefinition;
           
            if (value.constant_value is IArrayConstantNode)
                CreateArrayForClassField(cur_ti.static_cnstr.Body.GetILProcessor(), fb, ti, value.constant_value as IArrayConstantNode, value.type);
            
        }

        internal void GenerateInitCodeForField(SemanticTree.ICommonClassFieldNode value)
        {
            TypeInfo ti = helper.GetTypeReference(value.type), cur_ti = helper.GetTypeReference(value.comperehensive_type);
            var fb = helper.GetField(value).fi as Mono.Cecil.FieldDefinition;
            if (value.type.is_generic_parameter && value.inital_value == null)
            {
                CreateRuntimeInitCodeWithCheck((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, value.type as ICommonTypeNode);
            }
            if (ti.is_arr)
            {
                if (value.inital_value == null || value.inital_value is IArrayConstantNode)
                    CreateArrayForClassField((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, ti, value.inital_value as IArrayConstantNode, value.type);
                else if (value.inital_value is IArrayInitializer)
                    CreateArrayForClassField((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, ti, value.inital_value as IArrayInitializer, value.type);
            }
            else if (value.inital_value is IArrayConstantNode)
                CreateArrayForClassField((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, ti, value.inital_value as IArrayConstantNode, value.type);
            else if (value.inital_value is IArrayInitializer)
                CreateArrayForClassField((cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), fb, ti, value.inital_value as IArrayInitializer, value.type);
            else
                if (value.type.is_value_type && value.inital_value == null || value.inital_value is IConstantNode && !(value.inital_value is INullConstantNode))
                    AddInitCall(fb, (cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor(), ti.init_meth, ti.def_cnstr, value.inital_value as IConstantNode);
            in_var_init = true;
            GenerateInitCode(value, (cur_ti.init_meth as Mono.Cecil.MethodDefinition).Body.GetILProcessor());
            in_var_init = false;
        }

        public override void visit(SemanticTree.ICommonNamespaceVariableNode value)
        {

        }

        public override void visit(SemanticTree.ILocalVariableNode value)
        {

        }

        public override void visit(SemanticTree.IVAriableDefinitionNode value)
        {

        }

        //перевод символьной константы
        //команда ldc_i4_s
        //is_dot_expr - признак того, что после этого выражения
        //идет точка (например 'a'.ToString)
        public override void visit(SemanticTree.ICharConstantNode value)
        {
            NETGeneratorTools.LdcIntConst(il, value.constant_value);

            if (is_dot_expr == true)
            {
                //определяем временную переменную
                var lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Char);
                il.Body.Variables.Add(lb);
                //сохраняем в переменной симв. константу
                il.Emit(OpCodes.Stloc, lb);
                //кладем адрес этой переменной
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IFloatConstantNode value)
        {
            il.Emit(OpCodes.Ldc_R4, value.constant_value);
            if (is_dot_expr)
                NETGeneratorTools.CreateLocalAndLdloca(il, mb.TypeSystem.Single);
        }

        public override void visit(SemanticTree.IDoubleConstantNode value)
        {
            il.Emit(OpCodes.Ldc_R8, value.constant_value);
            if (is_dot_expr)
                NETGeneratorTools.CreateLocalAndLdloca(il, mb.TypeSystem.Double);
        }

        //перевод целой константы
        //команда ldc_i4
        private void PushIntConst(int e)
        {
            PushIntConst(il, e);
        }

        private void PushIntConst(Mono.Cecil.Cil.ILProcessor il, int e)
        {
            NETGeneratorTools.LdcIntConst(il, e);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        //ivan
        private void PushFloatConst(float value)
        {
            il.Emit(OpCodes.Ldc_R4, value);
        }

        private void PushDoubleConst(double value)
        {
            il.Emit(OpCodes.Ldc_R8, value);
        }

        private void PushCharConst(char value)
        {
            NETGeneratorTools.LdcIntConst(il, value);
        }

        private void PushStringConst(string value)
        {
            il.Emit(OpCodes.Ldstr, value);
        }

        private void PushByteConst(byte value)
        {
            NETGeneratorTools.LdcIntConst(il, value);
        }

        private void PushLongConst(long value)
        {
            il.Emit(OpCodes.Ldc_I8, (long)value);
        }

        private void PushShortConst(short value)
        {
            NETGeneratorTools.LdcIntConst(il, value);
        }

        private void PushUShortConst(ushort value)
        {
            NETGeneratorTools.LdcIntConst(il, value);
        }

        private void PushUIntConst(uint value)
        {
            NETGeneratorTools.LdcIntConst(il, (int)value);
        }

        private void PushULongConst(ulong value)
        {
            long l = (long)(value & 0x7FFFFFFFFFFFFFFF);
            if ((value & 0x8000000000000000) != 0)
            {
                long l2 = 0x4000000000000000 << 1;
                l |= l2;
            }
            il.Emit(OpCodes.Ldc_I8, l);
        }

        private void PushSByteConst(sbyte value)
        {
            NETGeneratorTools.LdcIntConst(il, value);
        }

        private void PushBoolConst(bool value)
        {
            if (value)
                il.Emit(OpCodes.Ldc_I4_1);
            else
                il.Emit(OpCodes.Ldc_I4_0);
        }
        //\ivan
        public override void visit(SemanticTree.IIntConstantNode value)
        {
            PushIntConst(value.constant_value);
        }

        //перевод long константы
        //команда ldc_i8
        public override void visit(SemanticTree.ILongConstantNode value)
        {
            il.Emit(OpCodes.Ldc_I8, (long)value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int64);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }
        //перевод byte константы
        //команда ldc_i4_S
        public override void visit(SemanticTree.IByteConstantNode value)
        {
            NETGeneratorTools.LdcIntConst(il, value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Byte);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IShortConstantNode value)
        {
            NETGeneratorTools.LdcIntConst(il, value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int16);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IUShortConstantNode value)
        {
            NETGeneratorTools.LdcIntConst(il, value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.UInt16);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IUIntConstantNode value)
        {
            NETGeneratorTools.LdcIntConst(il, (int)value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.UInt32);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IULongConstantNode value)
        {
            long l = (long)(value.constant_value & 0x7FFFFFFFFFFFFFFF);
            if ((value.constant_value & 0x8000000000000000) != 0)
            {
                long l2 = 0x4000000000000000 << 1;
                l |= l2;
            }
            il.Emit(OpCodes.Ldc_I8, l);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.UInt64);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.ISByteConstantNode value)
        {
            //il.Emit(OpCodes.Ldc_I4, (int)value.constant_value);
            NETGeneratorTools.LdcIntConst(il, value.constant_value);
            if (is_dot_expr == true)
            {
                Mono.Cecil.Cil.VariableDefinition lb = null;
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.SByte);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        //перевод булевской константы
        //команда ldc_i4_0/1
        public override void visit(SemanticTree.IBoolConstantNode value)
        {
            if (value.constant_value == true)
                il.Emit(OpCodes.Ldc_I4_1);
            else
                il.Emit(OpCodes.Ldc_I4_0);
            if (is_dot_expr == true)
            {
                var lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Boolean);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
        }

        public override void visit(SemanticTree.IConstantNode value)
        {

        }

        private void PushParameter(int pos)
        {
            switch (pos)
            {
                case 0: il.Emit(OpCodes.Ldarg_0); break;
                case 1: il.Emit(OpCodes.Ldarg_1); break;
                case 2: il.Emit(OpCodes.Ldarg_2); break;
                case 3: il.Emit(OpCodes.Ldarg_3); break;
                default:
                    if (pos <= 255)
                        il.Emit(OpCodes.Ldarg_S, (byte)pos);
                    else
                        //здесь надо быть внимательнее
                        il.Emit(OpCodes.Ldarg, (short)pos);
                    break;
            }
        }

        private void PushParameterAddress(int pos)
        {
            if (pos <= byte.MaxValue)
                il.Emit(OpCodes.Ldarga_S, (byte)pos);
            else
                il.Emit(OpCodes.Ldarga, pos);
        }

        bool must_push_addr;

        //перевод ссылки на параметр
        public override void visit(SemanticTree.ICommonParameterReferenceNode value)
        {
            must_push_addr = false;//должен ли упаковываться, но это если после идет точка
            if (is_dot_expr)//если после идет точка
            {
                if (value.type.is_value_type || value.type.is_generic_parameter)
                {
                    if (!(is_field_reference && value.type.is_generic_parameter && value.type.base_type != null && value.type.base_type.is_class && value.type.base_type.base_type != null))
                        must_push_addr = true;
                    else if (value.type.is_generic_parameter && virtual_method_call && !is_field_reference)
                    {
                        must_push_addr = true;
                        virtual_method_call = false;
                    }
                        
                }
                else if (value.conversion_type != null && (value.conversion_type.is_generic_parameter))
                {
                    if (!(value.conversion_type.is_generic_parameter && value.conversion_type.base_type != null && value.conversion_type.base_type.is_class && value.conversion_type.base_type.base_type != null))
                        must_push_addr = true;
                }
            }
            ParamInfo pi = helper.GetParameter(value.parameter);
            if (pi.kind == ParamKind.pkNone)
            {
                //этот параметр яв-ся локальным
                Mono.Cecil.ParameterDefinition pb = pi.pb;
                //это хрень с позициями меня достает
                byte pos = (byte)(pb.Index);
                if (is_constructor || !cur_meth.IsStatic)
                    pos = (byte)pb.Index;
                else
                    pos = (byte)(pb.Index);
                if (value.parameter.parameter_type == parameter_type.value)
                {
                    //напомним, что is_addr - передается ли он в качестве факт. параметра по ссылке
                    if (!is_addr)
                    {
                        if (must_push_addr)
                        {
                            //здесь кладем адрес параметра
                            PushParameterAddress(pos);
                        }
                        else
                            PushParameter(pos);
                    }
                    else
                        PushParameterAddress(pos);
                }
                else
                {
                    //это var-параметр
                    PushParameter(pos);
                    if (!is_addr && !must_push_addr)
                    {
                        TypeInfo ti = helper.GetTypeReference(value.parameter.type);
                        NETGeneratorTools.PushParameterDereference(il, ti.tp);
                    }
                }
            }
            else
            {
                //это параметр нелокальный
                var fb = pi.fb;
                MethInfo cur_mi = smi.Peek();
                int dist = smi.Peek().num_scope - pi.meth.num_scope;
                //проходимся по цепочке записей активации
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                    cur_mi = cur_mi.up_meth;
                }
                if (value.parameter.parameter_type == parameter_type.value)
                {
                    if (!is_addr)
                    {
                        if (!must_push_addr) il.Emit(OpCodes.Ldfld, fb);
                        else il.Emit(OpCodes.Ldflda, fb);
                    }
                    else il.Emit(OpCodes.Ldflda, fb);
                }
                else
                {
                    il.Emit(OpCodes.Ldfld, fb);
                    if (!is_addr && !must_push_addr)
                    {
                        TypeInfo ti = helper.GetTypeReference(value.parameter.type);
                        NETGeneratorTools.PushParameterDereference(il, ti.tp);
                    }
                }
            }
        }

        //доступ к статическому откомпилированному типу
        public override void visit(SemanticTree.IStaticCompiledFieldReferenceNode value)
        {
            //если у поля нет постоянное значение
            if (!mb.ImportReference(value.static_field.compiled_field).Resolve().IsLiteral)
            {
                if (!is_addr)
                {
                    if (is_dot_expr && value.static_field.compiled_field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldsflda, mb.ImportReference(value.static_field.compiled_field));
                    }
                    else 
                        il.Emit(OpCodes.Ldsfld, mb.ImportReference(value.static_field.compiled_field));
                }
                else 
                    il.Emit(OpCodes.Ldsflda, mb.ImportReference(value.static_field.compiled_field));

            }
            else
            {
                //иначе кладем константу
                NETGeneratorTools.PushLdc(il, mb.ImportReference(value.static_field.compiled_field.FieldType), mb.ImportReference(value.static_field.compiled_field).Resolve().Constant);
                if (is_dot_expr)
                {
                    //нужно упаковать
                    il.Emit(OpCodes.Box, mb.ImportReference(value.static_field.compiled_field.FieldType));
                }
            }
        }

        //доступ к откомпилированному нестатическому полю
        public override void visit(SemanticTree.ICompiledFieldReferenceNode value)
        {
            bool tmp_dot = is_dot_expr;
            if (!tmp_dot)
                is_dot_expr = true;
            if (!mb.ImportReference(value.field.compiled_field).Resolve().IsLiteral)
            {
                is_field_reference = true;
                value.obj.visit(this);
                if (!is_addr)
                {
                    if (tmp_dot && value.field.compiled_field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldflda, mb.ImportReference(value.field.compiled_field));
                    }
                    else 
                        il.Emit(OpCodes.Ldfld, mb.ImportReference(value.field.compiled_field));
                }
                else 
                    il.Emit(OpCodes.Ldflda, mb.ImportReference(value.field.compiled_field));
                is_field_reference = false;
            }
            else
            {
                NETGeneratorTools.PushLdc(il, mb.ImportReference(value.field.compiled_field.FieldType), mb.ImportReference(value.field.compiled_field).Resolve().Constant);
                if (tmp_dot)
                {
                    il.Emit(OpCodes.Box, mb.ImportReference(value.field.compiled_field.FieldType));
                }
            }
            if (!tmp_dot)
            {
                is_dot_expr = false;
            }
        }

        public override void visit(SemanticTree.IStaticCommonClassFieldReferenceNode value)
        {
            bool tmp_dot = is_dot_expr;
            FldInfo fi_info = helper.GetField(value.static_field);
            var fi = fi_info.fi;
            if (!is_addr)
            {
                if (tmp_dot)
                {
                    if (fi_info.field_type.IsValueType || fi_info.field_type.IsGenericParameter)
                    {
                        il.Emit(OpCodes.Ldsflda, fi);
                    }
                    else
                        il.Emit(OpCodes.Ldsfld, fi);
                }
                else
                    il.Emit(OpCodes.Ldsfld, fi);
            }
            else
                il.Emit(OpCodes.Ldsflda, fi);
        }

        public override void visit(SemanticTree.ICommonClassFieldReferenceNode value)
        {
            bool tmp_dot = is_dot_expr;
            if (!tmp_dot)
                is_dot_expr = true;
            bool temp_is_addr = is_addr;
            is_addr = false;
            //is_dot_expr = false;
            is_field_reference = true;
            bool tmp_virtual_method_call = virtual_method_call;
            virtual_method_call = false;
            value.obj.visit(this);
            if (value.obj is ICommonClassFieldReferenceNode)
                is_field_reference = true;
            virtual_method_call = tmp_virtual_method_call;
            is_addr = temp_is_addr;
            FldInfo fi_info = helper.GetField(value.field);
#if DEBUG
            /*if (value.field.name == "XYZW")
            {
                var y = value.field.GetHashCode();
            } */
#endif
            var fi = fi_info.fi;
            if (!is_addr)
            {
                if (tmp_dot)
                {
                    if (fi_info.field_type.IsValueType || fi_info.field_type.IsGenericParameter)
                    {
                        if (is_field_reference && !virtual_method_call && (value.type.is_generic_parameter && value.type.base_type != null && value.type.base_type.is_class && value.type.base_type.base_type != null
                            || value.conversion_type != null && value.conversion_type.is_generic_parameter && value.conversion_type.base_type != null && value.conversion_type.base_type.is_class && value.conversion_type.base_type.base_type != null))
                            il.Emit(OpCodes.Ldfld, fi);
                        else
                            il.Emit(OpCodes.Ldflda, fi);
                    }
                    else
                        il.Emit(OpCodes.Ldfld, fi);
                }
                else
                    il.Emit(OpCodes.Ldfld, fi);
            }
            else
                il.Emit(OpCodes.Ldflda, fi);

            if (!tmp_dot)
            {
                is_dot_expr = false;
            }
            is_field_reference = false;
        }

        public override void visit(SemanticTree.INamespaceVariableReferenceNode value)
        {
            VarInfo vi = helper.GetVariable(value.variable);
            if (vi == null)
            {
                ConvertGlobalVariable(value.variable);
                vi = helper.GetVariable(value.variable);
            }
            var fb = vi.fb;
            if (is_addr == false)
            {
                if (is_dot_expr == true)
                {
                    if (fb.FieldType.IsValueType == true)
                    {
                        il.Emit(OpCodes.Ldsflda, fb);
                    }
                    else
                        il.Emit(OpCodes.Ldsfld, fb);
                }
                else
                    il.Emit(OpCodes.Ldsfld, fb);
            }
            else il.Emit(OpCodes.Ldsflda, fb);

        }

        //чтобы перевести переменную, нужно до фига проверок
        public override void visit(SemanticTree.ILocalVariableReferenceNode value)
        {
            VarInfo vi = helper.GetVariable(value.variable);
            if (vi == null)
            {
                ConvertLocalVariable(value.variable, false, 0, 0);
                vi = helper.GetVariable(value.variable);
            }
            if (vi.kind == VarKind.vkLocal)//если локальная
            {
                var lb = vi.lb;
                if (!is_addr)//если это факт. var-параметр
                {
                    if (is_dot_expr) //если после перем. в выражении стоит точка
                    {
                        if (lb.VariableType.IsGenericParameter)
                        {
                            //il.Emit(OpCodes.Ldloc, lb);
                            //il.Emit(OpCodes.Box, lb.LocalType);
                            if (is_field_reference && value.type.is_generic_parameter && value.type.base_type != null && value.type.base_type.is_class && value.type.base_type.base_type != null)
                                il.Emit(OpCodes.Ldloc, lb);//#2247 (where ssylochnyj tip): kakogo-to cherta dlja obrashenija k polu nado klast znachenie, a ne adres. dlja vyzova metoda vsegda adres
                            else
                                il.Emit(OpCodes.Ldloca, lb);
                        }
                        else
                            if (lb.VariableType.IsValueType)
                        {
                            il.Emit(OpCodes.Ldloca, lb);//если перем. размерного типа кладем ее адрес
                        }
                        else
                        {

                            il.Emit(OpCodes.Ldloc, lb);
                        }
                    }
                    else il.Emit(OpCodes.Ldloc, lb);
                }
                else il.Emit(OpCodes.Ldloca, lb);//в этом случае перем. - фактический var-параметр процедуры
            }
            else if (vi.kind == VarKind.vkNonLocal) //переменная нелокальная
            {
                var fb = vi.fb; //значит, это поля класса-обертки
                MethInfo cur_mi = smi.Peek();
                int dist = smi.Peek().num_scope - vi.meth.num_scope;//получаем разность глубин вложенности
                il.Emit(OpCodes.Ldloc, cur_mi.frame); //кладем объект класса-обертки
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent); //проходимся по цепочке
                    cur_mi = cur_mi.up_meth;
                }
                if (is_addr == false) //здесь уже ясно
                {
                    if (is_dot_expr == true) //в выражении после стоит точка
                    {
                        if (fb.FieldType.IsValueType == true)
                        {
                            il.Emit(OpCodes.Ldflda, fb);//для размерного значения кладем адрес
                        }
                        else il.Emit(OpCodes.Ldfld, fb);
                    }
                    else
                        il.Emit(OpCodes.Ldfld, fb);
                }
                else il.Emit(OpCodes.Ldflda, fb);
            }
        }

        public override void visit(SemanticTree.IAddressedExpressionNode value)
        {

        }

        public override void visit(SemanticTree.IProgramNode value)
        {

        }

        public override void visit(SemanticTree.IDllNode value)
        {

        }

        public override void visit(SemanticTree.ICompiledNamespaceNode value)
        {

        }

        public override void visit(SemanticTree.ICommonNamespaceNode value)
        {

        }

        public override void visit(SemanticTree.INamespaceNode value)
        {

        }

        private void ConvertStatementsListWithoutFirstStatement(SemanticTree.IStatementsListNode value)
        {
            if (save_debug_info)
            {
                if (gen_left_brackets)
                    MarkSequencePoint(value.LeftLogicalBracketLocation);
                else
                {
                    var instr = il.Body.Instructions.Last();
                    var point = new Mono.Cecil.Cil.SequencePoint(instr, doc)
                    {
                        StartLine = 0xFeeFee, StartColumn = 0xFeeFee,
                        EndLine = 0xFeeFee, EndColumn = 0xFeeFee
                    };

                    il.Body.Method.DebugInformation.SequencePoints.Add(point);
                }
                //il.MarkSequencePoint(doc,0xFFFFFF,0xFFFFFF,0xFFFFFF,0xFFFFFF);
                il.Emit(OpCodes.Nop);
            }
            ILocalBlockVariableNode[] localVariables = value.LocalVariables;
            for (int i = 0; i < localVariables.Length; i++)
            {
                ConvertLocalVariable(localVariables[i], true, value.Location.begin_line_num, value.Location.end_line_num);
            }
            IStatementNode[] statements = value.statements;
            if (statements.Length == 0)
            {
                if (save_debug_info)
                    il.Emit(OpCodes.Nop);
                return;
            }

            for (int i = 1; i < statements.Length - 1; i++)
            {
                ConvertStatement(statements[i]);
            }

            if (save_debug_info)
                if (statements[statements.Length - 1] is SemanticTree.IReturnNode)
                    //если return не имеет location то метим точку на месте закрывающей логической скобки
                    if (statements[statements.Length - 1].Location == null)
                        MarkSequencePoint(value.RightLogicalBracketLocation);

            ConvertStatement(statements[statements.Length - 1]);

            //TODO: переделать. сдель функцию которая ложет ret и MarkSequencePoint
            if (save_debug_info && !(statements[statements.Length - 1] is SemanticTree.IReturnNode))
            {
                //если почледний оператор не Return то пометить закрывающуюю логическую скобку
                if (gen_right_brackets)
                    MarkSequencePoint(value.RightLogicalBracketLocation);
                //il.Emit(OpCodes.Nop);
            }
        }

        public override void visit(SemanticTree.IStatementsListNode value)
        {
            IStatementNode[] statements = value.statements;
            if (save_debug_info)
            {
                if (gen_left_brackets || value.LeftLogicalBracketLocation == null)
                    MarkSequencePoint(value.LeftLogicalBracketLocation);
                else
                {
                    var instr = il.Body.Instructions.Last();
                    var point = new Mono.Cecil.Cil.SequencePoint(instr, doc)
                    {
                        StartLine = 0xFeeFee, StartColumn = 0xFeeFee,
                        EndLine = 0xFeeFee, EndColumn = 0xFeeFee
                    };

                    il.Body.Method.DebugInformation.SequencePoints.Add(point);
                }
                il.Emit(OpCodes.Nop);
            }
            
            ILocalBlockVariableNode[] localVariables = value.LocalVariables;
            for (int i = 0; i < localVariables.Length; i++)
            {
                if (value.Location != null && value.LeftLogicalBracketLocation == null && statements.Length > 0 && statements[statements.Length - 1].Location != null &&
                    statements[statements.Length - 1].Location.begin_line_num == value.Location.begin_line_num)
                    ConvertLocalVariable(localVariables[i], true, statements[statements.Length - 1].Location.begin_line_num, statements[statements.Length - 1].Location.end_line_num);
                else if (value.Location != null)
                    ConvertLocalVariable(localVariables[i], true, value.Location.begin_line_num, value.Location.end_line_num);
                else
                    ConvertLocalVariable(localVariables[i], false, 0, 0);
            }

            if (statements.Length == 0)
            {
                if (save_debug_info)
                    il.Emit(OpCodes.Nop);
                return;
            }
            next_location = null;
            for (int i = 0; i < statements.Length - 1; i++)
            {
                if (i < statements.Length - 2)
                    next_location = statements[i + 1].Location;
                else
                    next_location = value.RightLogicalBracketLocation;
                ConvertStatement(statements[i]);
            }

            if (save_debug_info)
                if (statements[statements.Length - 1] is SemanticTree.IReturnNode)
                    //если return не имеет location то метим точку на месте закрывающей логической скобки
                    if (statements[statements.Length - 1].Location == null)
                        MarkSequencePoint(value.RightLogicalBracketLocation);
            next_location = value.RightLogicalBracketLocation;
            ConvertStatement(statements[statements.Length - 1]);

            //TODO: переделать. сдель функцию которая ложет ret и MarkSequencePoint
            if (save_debug_info && !(statements[statements.Length - 1] is SemanticTree.IReturnNode))
            {
                //если почледний оператор не Return то пометить закрывающуюю логическую скобку
                if (gen_right_brackets)
                    MarkSequencePoint(value.RightLogicalBracketLocation);
                //il.Emit(OpCodes.Nop);
            }
        }

        private bool is_assign(basic_function_type bft)
        {
            switch (bft)
            {
                case basic_function_type.iassign:
                case basic_function_type.bassign:
                case basic_function_type.lassign:
                case basic_function_type.sassign:
                case basic_function_type.dassign:
                case basic_function_type.fassign:
                case basic_function_type.boolassign:
                case basic_function_type.objassign:
                case basic_function_type.charassign: return true;
            }
            return false;
        }

        private void ConvertExpression(IExpressionNode value)
        {
            make_next_spoint = false;
            value.visit(this);
        }

        private bool BeginOnForNode(IStatementNode value)
        {
            //if (value is IForNode) return true;
            IStatementsListNode stats = value as IStatementsListNode;
            if (stats == null) return false;
            if (stats.statements.Length == 0) return false;
            //if (stats.statements[0] is IForNode) return true;
            return false;
        }

        //перевод statement-а
        private void ConvertStatement(IStatementNode value)
        {
            make_next_spoint = true;
            if (save_debug_info /*&& !(value is IForNode)*/)
                MarkSequencePoint(value.Location);
            make_next_spoint = false;
            value.visit(this);
            make_next_spoint = true;
            //нужно для очистки стека после вызова функции в качестве процедуры
            //ssyy добавил
            //если вызов конструктора предка, то стек не очищаем
            if (!(
                (value is IFunctionCallNode) && ((IFunctionCallNode)value).last_result_function_call ||
                (value is ICompiledConstructorCall) && !((ICompiledConstructorCall)value).new_obj_awaited() ||
                (value is ICommonConstructorCall) && !((ICommonConstructorCall)value).new_obj_awaited()
            ))
                //\ssyy
                if ((value is IFunctionCallNode) && !(value is IBasicFunctionCallNode && (value as IBasicFunctionCallNode).basic_function.basic_function_type != basic_function_type.none))
                {
                    IFunctionCallNode fc = value as IFunctionCallNode;
                    if (fc.function.return_value_type != null)
                    {
                        ICompiledTypeNode ct = fc.function.return_value_type as ICompiledTypeNode;
                        if ((ct == null) || (ct != null && (ct.compiled_type.FullName != mb.TypeSystem.Void.FullName)))
                            il.Emit(OpCodes.Pop);
                    }
                }
        }

        private bool gen_right_brackets = true;
        private bool gen_left_brackets = true;
        public override void visit(SemanticTree.IForNode value)
        {
            Instruction l1 = il.Create(OpCodes.Nop);
            Instruction l2 = il.Create(OpCodes.Nop);
            Instruction lcont = il.Create(OpCodes.Nop);
            Instruction lbreak = il.Create(OpCodes.Nop);
            bool tmp = save_debug_info;
            save_debug_info = false;
            if (value.initialization_statement != null)
                ConvertStatement(value.initialization_statement);
            save_debug_info = tmp;
            if (value.init_while_expr != null)
            {
                value.init_while_expr.visit(this);
                il.Emit(OpCodes.Brfalse, lbreak);
            }
            else
                il.Emit(OpCodes.Br, l1);
            il.Append(l2);
            labels.Push(lbreak);
            clabels.Push(lcont);
            bool tmp_rb = gen_right_brackets;
            bool tmp_lb = gen_left_brackets;
            gen_right_brackets = false;
            gen_left_brackets = false;
            ConvertStatement(value.body);
            gen_right_brackets = tmp_rb;
            gen_left_brackets = tmp_lb;
            il.Append(lcont);
            tmp = save_debug_info;

            if (value.init_while_expr == null)
            {
                save_debug_info = false;
                //				MarkSequencePoint(il,0xFeeFee,0xFeeFee,0xFeeFee,0xFeeFee);
                ConvertStatement(value.increment_statement);
                save_debug_info = tmp;
            }
            il.Append(l1);
            MarkSequencePoint(il, value.increment_statement.Location);
            value.while_expr.visit(this);
            //if (!value.IsBoolCycle)
            if (value.init_while_expr == null)
                il.Emit(OpCodes.Brtrue, l2);
            else
            {
                Instruction l3 = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Brfalse, l3);
                save_debug_info = false;
                //				MarkSequencePoint(il,0xFeeFee,0xFeeFee,0xFeeFee,0xFeeFee);
                ConvertStatement(value.increment_statement);
                save_debug_info = tmp;
                il.Emit(OpCodes.Br, l2);
                il.Append(l3);
            }
            il.Append(lbreak);
            labels.Pop();
            clabels.Pop();
        }

        public override void visit(SemanticTree.IRepeatNode value)
        {
            Instruction TrueLabel, FalseLabel;
            TrueLabel = il.Create(OpCodes.Nop);
            FalseLabel = il.Create(OpCodes.Nop);
            il.Append(TrueLabel);
            labels.Push(FalseLabel);//break
            clabels.Push(TrueLabel);//continue
            if (save_debug_info) MarkForCicles(value.Location, value.body.Location);
            ConvertStatement(value.body);
            value.condition.visit(this);
            il.Emit(OpCodes.Brfalse, TrueLabel);
            il.Append(FalseLabel);
            clabels.Pop();
            labels.Pop();
        }

        private void MarkForCicles(ILocation loc, ILocation body_loc)
        {
            if (loc != null)
                if (loc.begin_line_num == body_loc.end_line_num) MarkSequencePoint(il, body_loc);
        }

        public override void visit(SemanticTree.IWhileNode value)
        {
            Instruction TrueLabel, FalseLabel;
            TrueLabel = il.Create(OpCodes.Nop);
            FalseLabel = il.Create(OpCodes.Nop);
            il.Append(TrueLabel);
            value.condition.visit(this);
            il.Emit(OpCodes.Brfalse, FalseLabel);
            labels.Push(FalseLabel);//break
            clabels.Push(TrueLabel);//continue
            bool tmp_lb = gen_left_brackets;
            gen_left_brackets = false;
            ConvertStatement(value.body);
            gen_left_brackets = tmp_lb;
            il.Emit(OpCodes.Br, TrueLabel);
            il.Append(FalseLabel);
            clabels.Pop();
            labels.Pop();
        }

        public override void visit(SemanticTree.ITryBlockNode value)
        {
            Instruction exBl = il.BeginExceptionBlock();
            var safe_block = EnterSafeBlock();
            ConvertStatement(value.TryStatements);
            LeaveSafeBlock(safe_block);
            if (value.ExceptionFilters.Length != 0)
            {
                foreach (SemanticTree.IExceptionFilterBlockNode iefbn in value.ExceptionFilters)
                {
                    Mono.Cecil.TypeReference typ;
                    if (iefbn.ExceptionType != null)
                        typ = helper.GetTypeReference(iefbn.ExceptionType).tp;
                    else
                        typ = mb.ImportReference(TypeFactory.ExceptionType);
                    il.BeginCatchBlock(typ);

                    if (iefbn.ExceptionInstance != null)
                    {
                        var lb = new Mono.Cecil.Cil.VariableDefinition(typ);
                        il.Body.Variables.Add(lb);
                        helper.AddVariable(iefbn.ExceptionInstance.Variable, lb);
                        if (save_debug_info && iefbn.ExceptionInstance.Location != null)
                            il.Body.Method.DebugInformation.Scope.Variables.Add(new Mono.Cecil.Cil.VariableDebugInformation(lb, iefbn.ExceptionInstance.Variable.name + ":" +
                                iefbn.ExceptionInstance.Location.begin_line_num + ":" +
                                ((iefbn.ExceptionHandler != null && iefbn.ExceptionHandler.Location != null) ? iefbn.ExceptionHandler.Location.end_line_num : iefbn.ExceptionInstance.Location.end_line_num))
                            );
                        il.Emit(OpCodes.Stloc, lb);
                    }
                    else
                    {
                        il.Emit(OpCodes.Pop);
                    }
                    safe_block = EnterSafeBlock();
                    ConvertStatement(iefbn.ExceptionHandler);
                    LeaveSafeBlock(safe_block);
                }
            }
            if (value.FinallyStatements != null)
            {
                il.BeginFinallyBlock();
                safe_block = EnterSafeBlock();
                ConvertStatement(value.FinallyStatements);
                LeaveSafeBlock(safe_block);
            }
            il.EndExceptionBlock();
        }

        public override void visit(ILabeledStatementNode value)
        {
            Instruction lab = helper.GetLabel(value.label, il);
            il.Append(lab);
            ConvertStatement(value.statement);
        }

        public override void visit(IGotoStatementNode value)
        {
            Instruction lab = helper.GetLabel(value.label, il);
            if (safe_block)
                il.Emit(OpCodes.Leave, lab);
            else
                il.Emit(OpCodes.Br, lab);
        }

        private Stack<Instruction> if_stack = new Stack<Instruction>();

        private bool contains_only_if(IStatementNode stmt)
        {
            return stmt is IIfNode || stmt is ISwitchNode || stmt is IStatementsListNode && (stmt as IStatementsListNode).statements.Length == 1 &&
                ((stmt as IStatementsListNode).statements[0] is IIfNode || (stmt as IStatementsListNode).statements[0] is ISwitchNode);
        }

        public override void visit(SemanticTree.IIfNode value)
        {
            Instruction FalseLabel, EndLabel;
            FalseLabel = il.Create(OpCodes.Nop);
            bool end_label_def = false;
            bool is_first_if = false;
            if (contains_only_if(value.then_body))
            {
                if (if_stack.Count == 0)
                {
                    EndLabel = il.Create(OpCodes.Nop);
                    end_label_def = true;
                    is_first_if = true;
                    if_stack.Push(EndLabel);
                }
                else
                    EndLabel = if_stack.Peek();
            }
            else if (if_stack.Count > 0 && !contains_only_if(value.then_body))
                EndLabel = if_stack.Pop();
            else
            {
                end_label_def = true;
                EndLabel = il.Create(OpCodes.Nop);
            }
            value.condition.visit(this);
            il.Emit(OpCodes.Brfalse, FalseLabel);

            ConvertStatement(value.then_body);
            il.Emit(OpCodes.Br, EndLabel);
            if (value.else_body == null && next_location != null)
            {
                var seqPoint = new Mono.Cecil.Cil.SequencePoint(il.Body.Instructions.Last(), doc)
                {
                    StartLine = next_location.begin_line_num, StartColumn = 1,
                    EndLine = next_location.begin_line_num, EndColumn = next_location.begin_column_num
                };
                il.Body.Method.DebugInformation.SequencePoints.Add(seqPoint);
            }
            il.Append(FalseLabel);
            if (value.else_body != null)
                ConvertStatement(value.else_body);
            if (end_label_def)
            {
                if (is_first_if)
                    if_stack.Clear();
                il.Append(EndLabel);
            }
        }

        public override void visit(IWhileBreakNode value)
        {
            Instruction l = labels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IRepeatBreakNode value)
        {
            Instruction l = labels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IForBreakNode value)
        {
            Instruction l = labels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IWhileContinueNode value)
        {
            Instruction l = clabels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IRepeatContinueNode value)
        {
            Instruction l = clabels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IForContinueNode value)
        {
            Instruction l = clabels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IForeachBreakNode value)
        {
            Instruction l = labels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(IForeachContinueNode value)
        {
            Instruction l = clabels.Peek();
            if (safe_block)
                il.Emit(OpCodes.Leave, l);
            else
                il.Emit(OpCodes.Br, l);
        }

        public override void visit(SemanticTree.ICompiledMethodNode value)
        {

        }

        //перевод тела конструктора
        private void ConvertConstructorBody(SemanticTree.ICommonMethodNode value)
        {
            num_scope++;
            //получаем билдер конструктора
            Mono.Cecil.MethodDefinition cnstr = helper.GetConstructorBuilder(value);
			Mono.Cecil.MethodDefinition tmp = cur_cnstr;
            cur_cnstr = cnstr;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            MethInfo copy_mi = null;
            if (value.functions_nodes.Length > 0)
            {
                copy_mi = ConvertCtorWithNested(value, cnstr);
            }
            if (value.functions_nodes.Length == 0)
            {
                if (!(value.function_code is SemanticTree.IRuntimeManagedMethodBody))
                {
                    il = cnstr.Body.GetILProcessor();
                    //переводим локальные переменные
                    ConvertCommonFunctionConstantDefinitions(value.constants);
                    ConvertLocalVariables(value.var_definition_nodes);
                    //вызываем метод $Init$ для инициализации массивов и проч.
                    /*if (value.polymorphic_state != polymorphic_state.ps_static && value.common_comprehensive_type.base_type is ICompiledTypeNode && (value.common_comprehensive_type.base_type as ICompiledTypeNode).compiled_type == TypeFactory.ObjectType)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Call, TypeFactory.ObjectType.GetConstructor(Type.EmptyTypes));
                    }*/
                    
                    if (value.polymorphic_state != polymorphic_state.ps_static)
                    {
                        init_call_awaited = true;
                        if (cur_type.IsValueType)
                        {
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Call, cur_ti.init_meth);
                        }
                        //il.Emit(OpCodes.Ldarg_0);
                        //il.Emit(OpCodes.Call, cur_ti.init_meth);
                    }
                    //переводим тело
                    is_constructor = true;
                    ConvertBody(value.function_code);
                    if (save_debug_info)
                    {
                        AddSpecialDebugVariables();
                    }
                    is_constructor = false;
                }
            }
            else
            {
                ConvertFunctionBody(value, copy_mi, false);
                //вызов статического метода-клона
                //при этом явно передается this
                il = cnstr.Body.GetILProcessor();
                if (save_debug_info)
                    MarkSequencePoint(il, 0xFFFFFF, 0, 0xFFFFFF, 0);
                ConvertStatement((value.function_code as IStatementsListNode).statements[0]);
                
                il.Emit(OpCodes.Ldarg_0);
                IParameterNode[] parameters = value.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i + 1 < 255)
                        il.Emit(OpCodes.Ldarg_S, (byte)(i + 1));
                    else
                        //здесь надо быть внимательнее
                        il.Emit(OpCodes.Ldarg, (short)(i + 1));
                }
                il.Emit(OpCodes.Call, copy_mi.mi);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
            }
            cur_cnstr = tmp;
            il = tmp_il;
            num_scope--;
        }

        public override void visit(SemanticTree.ICommonMethodNode value)
        {
            if (value.is_constructor == true)
            {
                ConvertConstructorBody(value);
                return;
            }
            if (value.function_code is IStatementsListNode)
            {
                IStatementNode[] statements = (value.function_code as IStatementsListNode).statements;
                if (statements.Length > 0 && (statements[0] is IExternalStatementNode || statements[0] is IPInvokeStatementNode))
                {
                    MakeAttribute(value);
                    return;
                }
                    
            }
            
            num_scope++;
            MakeAttribute(value);
            Mono.Cecil.MethodDefinition methb = helper.GetMethodBuilder(value);
			//helper.GetMethod(value)
			Mono.Cecil.MethodDefinition tmp = cur_meth;
            MethInfo copy_mi = null;
            cur_meth = methb;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            //если метод содержит вложенные процедуры
            if (value.functions_nodes.Length > 0)
            {
                copy_mi = ConvertMethodWithNested(value, methb);
            }
            //если нет вложенных процедур
            if (value.functions_nodes.Length == 0)
            {
                if (!(value.function_code is SemanticTree.IRuntimeManagedMethodBody))
                {
                    if (value.function_code != null && !value.common_comprehensive_type.IsInterface)
                    {
                        il = methb.Body.GetILProcessor();
                        ConvertLocalVariables(value.var_definition_nodes);
                        ConvertCommonFunctionConstantDefinitions(value.constants);
                        ConvertBody(value.function_code);
                        if (save_debug_info)
                        {
                            AddSpecialDebugVariables();
                        }
                        if (methb.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                            il.Emit(OpCodes.Ret);
                    }
                }
            }
            else
            {
                if (value.name.IndexOf("<yield_helper_error_checkerr>") == -1)
                    ConvertFunctionBody(value, copy_mi, true);
                //вызов статического метода-клона
                //при этом явно передается this
                il = methb.Body.GetILProcessor();
                if (save_debug_info)
                    MarkSequencePoint(il, 0xFFFFFF, 0, 0xFFFFFF, 0);
                if (value.polymorphic_state == polymorphic_state.ps_static)
                    il.Emit(OpCodes.Ldnull);
                else
                    il.Emit(OpCodes.Ldarg_0);
                IParameterNode[] parameters = value.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i + 1 < 255)
                        il.Emit(OpCodes.Ldarg_S, (byte)(i + 1));
                    else
                        //здесь надо быть внимательнее
                        il.Emit(OpCodes.Ldarg, (short)(i + 1));
                }
                il.Emit(OpCodes.Call, copy_mi.mi);
                il.Emit(OpCodes.Ret);
            }
            cur_meth = tmp;
            il = tmp_il;
            num_scope--;
            if (value.overrided_method != null && value.name.IndexOf('.') != -1)
            {
                Mono.Cecil.MethodReference mi = null;
                if (helper.GetMethod(value.overrided_method) != null)
                    mi = helper.GetMethod(value.overrided_method).mi;
                else
                    mi = mb.ImportReference((value.overrided_method as ICompiledMethodNode).method_info);

                methb.Overrides.Add(mi);
            }
        }

        private MethInfo ConvertCtorWithNested(SemanticTree.ICommonMethodNode meth, Mono.Cecil.MethodDefinition methodb)
        {
            num_scope++; //увеличиваем глубину обл. видимости
            Mono.Cecil.TypeDefinition tb = null, tmp_type = cur_type;
            Frame frm = null;
            //func.functions_nodes.Length > 0 - имеет вложенные
            //funcs.Count > 0 - сама вложенная
            frm = MakeAuxType(meth);//создаем запись активации
            tb = frm.tb;
            cur_type = tb;
            //получаем тип возвр. значения
            Mono.Cecil.TypeReference[] tmp_param_types = GetParamTypes(meth);
			Mono.Cecil.TypeReference[] param_types = new Mono.Cecil.TypeReference[tmp_param_types.Length + 1];
            //прибавляем тип this
            param_types[0] = methodb.DeclaringType;
            tmp_param_types.CopyTo(param_types, 1);

            //определяем метод
            Mono.Cecil.MethodDefinition methb = new Mono.Cecil.MethodDefinition("cnstr$" + uid++, MethodAttributes.Public | MethodAttributes.Static, methodb.DeclaringType);
            foreach (var paramType in param_types)
                methb.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            tb.Methods.Add(methb);
            MethInfo mi = null;
            //добавляем его фиктивно (т.е. не заносим в таблицы Helper-а) дабы остальные вызывали метод-заглушку
            mi = helper.AddFictiveMethod(meth, methb);
            mi.num_scope = num_scope;
            mi.disp = frm;//задаем запись активации
            mi.is_in_class = true;//указываем что метод в классе
            smi.Push(mi);//кладем его в стек
            Mono.Cecil.ParameterDefinition pb = null;
            int num = 1;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;

            il = methb.Body.GetILProcessor();
            IParameterNode[] parameters = meth.parameters;
            //if (ret_type != typeof(void)) mi.ret_val = il.DeclareLocal(ret_type);
            Mono.Cecil.FieldDefinition[] fba = new Mono.Cecil.FieldDefinition[parameters.Length];
            //явно определяем this
            pb = methb.Parameters[0];
            pb.Name = "$obj$";

            //та же самая чертовщина с глобальными параметрами, только здесь учитываем
            //наличие дополнительного параметра this
            for (int i = 0; i < parameters.Length; i++)
            {
                pb = methb.Parameters[i + num];
                pb.Name = parameters[i].name;
                if (parameters[i].is_params)
                    pb.CustomAttributes.Add(new CustomAttributeBuilder(mb.ImportReference(TypeFactory.ParamArrayAttributeCtor), new byte[] { 0x1, 0x0, 0x0, 0x0 }).Build());
                Mono.Cecil.FieldDefinition fb = null;
                if (parameters[i].parameter_type == parameter_type.value)
                {
                    fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, param_types[i + num]);
                    frm.tb.Fields.Add(fb);
                }
                else
                {
                    Mono.Cecil.TypeReference pt = param_types[i + num].Module.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                    if (pt == null) mb.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                    fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, pt);
                    frm.tb.Fields.Add(fb);
                }
                helper.AddGlobalParameter(parameters[i], fb).meth = smi.Peek();
                fba[i] = fb;
            }
            //переменная, хранящая запись активации
            Mono.Cecil.Cil.VariableDefinition frame = new Mono.Cecil.Cil.VariableDefinition(cur_type);
            il.Body.Variables.Add(frame);
            mi.frame = frame;
            if (doc != null) il.Body.Method.DebugInformation.Scope.Variables.Add(new Mono.Cecil.Cil.VariableDebugInformation(frame, "$disp$"));
            //создание записи активации
            il.Emit(OpCodes.Newobj, frm.cb);
            il.Emit(OpCodes.Stloc_0, frame);
            //заполнение полей параметрами
            for (int j = 0; j < fba.Length; j++)
            {
                il.Emit(OpCodes.Ldloc_0);
                if (parameters[j].parameter_type == parameter_type.value)
                {
                    il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                }
                il.Emit(OpCodes.Stfld, fba[j]);
            }
            funcs.Add(meth);
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = methb;
            //перевод нелокальных переменных
            ConvertNonLocalVariables(meth.var_definition_nodes, frm.mb);
            //перевод процедур, вложенных в метод
            ConvertNestedInMethodFunctionHeaders(meth.functions_nodes, methodb.DeclaringType);
            il = tmp_il;
            foreach (ICommonNestedInFunctionFunctionNode f in meth.functions_nodes)
                ConvertFunctionBody(f);
            if (frm != null)
                frm.mb.Body.GetILProcessor().Emit(OpCodes.Ret);
            cur_type = tmp_type;
            num_scope--;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
            return mi;
        }

        //перевод метода с вложенными процедурами
        private MethInfo ConvertMethodWithNested(SemanticTree.ICommonMethodNode meth, Mono.Cecil.MethodDefinition methodb)
        {
            num_scope++; //увеличиваем глубину обл. видимости
            Mono.Cecil.TypeDefinition tb = null, tmp_type = cur_type;
            Frame frm = null;
            //func.functions_nodes.Length > 0 - имеет вложенные
            //funcs.Count > 0 - сама вложенная
            frm = MakeAuxType(meth);//создаем запись активации
            tb = frm.tb;
            cur_type = tb;
            //получаем тип возвр. значения
            Mono.Cecil.TypeReference[] tmp_param_types = GetParamTypes(meth);
			Mono.Cecil.TypeReference[] param_types = new Mono.Cecil.TypeReference[tmp_param_types.Length + 1];
            //прибавляем тип this
            if (methodb.DeclaringType.IsValueType)
                param_types[0] = methodb.DeclaringType.MakeByReferenceType();
            else
                param_types[0] = methodb.DeclaringType;
            tmp_param_types.CopyTo(param_types, 1);

            //определяем метод
            Mono.Cecil.MethodDefinition methb = new Mono.Cecil.MethodDefinition(methodb.Name, MethodAttributes.Public | MethodAttributes.Static, methodb.ReturnType);
            tb.Methods.Add(methb);
            MethInfo mi = null;
            //добавляем его фиктивно (т.е. не заносим в таблицы Helper-а) дабы остальные вызывали метод-заглушку
            mi = helper.AddFictiveMethod(meth, methb);
            mi.num_scope = num_scope;
            mi.disp = frm;//задаем запись активации
            mi.is_in_class = true;//указываем что метод в классе
            smi.Push(mi);//кладем его в стек
            Mono.Cecil.ParameterDefinition pb = null;
            int num = 1;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;

            IParameterNode[] parameters = meth.parameters;
            il = methb.Body.GetILProcessor();
            //if (ret_type != typeof(void)) mi.ret_val = il.DeclareLocal(ret_type);
            Mono.Cecil.FieldDefinition[] fba = new Mono.Cecil.FieldDefinition[parameters.Length];
            //явно определяем this
            pb = new Mono.Cecil.ParameterDefinition("$obj$", ParameterAttributes.None, param_types[0]);
            methb.Parameters.Add(pb);

            //та же самая чертовщина с глобальными параметрами, только здесь учитываем
            //наличие дополнительного параметра this
            for (int i = 0; i < parameters.Length; i++)
            {
                pb = new Mono.Cecil.ParameterDefinition(parameters[i].name, ParameterAttributes.None, param_types[i + num]);
                methb.Parameters.Add(pb);
                Mono.Cecil.FieldDefinition fb = null;
                if (parameters[i].parameter_type == parameter_type.value)
                {
                    fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, param_types[i + num]);
                    frm.tb.Fields.Add(fb);
                }
                else
                {
                    Mono.Cecil.TypeReference pt = param_types[i + num].Module.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                    if (pt == null) mb.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                    fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, pt);
					frm.tb.Fields.Add(fb);
                }
                helper.AddGlobalParameter(parameters[i], fb).meth = smi.Peek();
                fba[i] = fb;
            }
            //переменная, хранящая запись активации
            Mono.Cecil.Cil.VariableDefinition frame = new Mono.Cecil.Cil.VariableDefinition(cur_type);
            il.Body.Variables.Add(frame);
            mi.frame = frame;
            if (doc != null) il.Body.Method.DebugInformation.Scope.Variables.Add(new Mono.Cecil.Cil.VariableDebugInformation(frame, "$disp$"));
			//создание записи активации
			il.Emit(OpCodes.Newobj, frm.cb);
            il.Emit(OpCodes.Stloc_0, frame);
            //заполнение полей параметрами
            for (int j = 0; j < fba.Length; j++)
            {
                il.Emit(OpCodes.Ldloc_0);
                if (parameters[j].parameter_type == parameter_type.value)
                {
                    il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_S, (byte)(j + 1));
                }
                il.Emit(OpCodes.Stfld, fba[j]);
            }
            funcs.Add(meth);
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = methb;
            ConvertCommonFunctionConstantDefinitions(meth.constants);
            //перевод нелокальных переменных
            ConvertNonLocalVariables(meth.var_definition_nodes, frm.mb);
            //перевод процедур, вложенных в метод
            ConvertNestedInMethodFunctionHeaders(meth.functions_nodes, methodb.DeclaringType);
            il = tmp_il;
            foreach (ICommonNestedInFunctionFunctionNode f in meth.functions_nodes)
                ConvertFunctionBody(f);
            if (frm != null)
                frm.mb.Body.GetILProcessor().Emit(OpCodes.Ret);
            cur_type = tmp_type;
            num_scope--;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
            return mi;
        }

        private void ConvertNestedInMethodFunctionHeaders(ICommonNestedInFunctionFunctionNode[] funcs, Mono.Cecil.TypeReference decl_type)
        {
            foreach (ICommonNestedInFunctionFunctionNode func in funcs)
            {
                ConvertNestedInMethodFunctionHeader(func, decl_type);
            }
        }

        private void ConvertNestedInMethodFunctionHeader(ICommonNestedInFunctionFunctionNode func, Mono.Cecil.TypeReference decl_type)
        {
            num_scope++; //увеличиваем глубину обл. видимости
            Mono.Cecil.TypeDefinition tb = null, tmp_type = cur_type;
            Frame frm = null;
            //func.functions_nodes.Length > 0 - имеет вложенные
            //funcs.Count > 0 - сама вложенная
            frm = MakeAuxType(func);//создаем запись активации
            tb = frm.tb;
            cur_type = tb;
            Mono.Cecil.TypeReference ret_type = null;
            //получаем тип возвр. значения
            if (func.return_value_type == null)
                ret_type = mb.TypeSystem.Void;
            else
                ret_type = helper.GetTypeReference(func.return_value_type).tp;
			//получаем типы параметров
			Mono.Cecil.TypeReference[] tmp_param_types = GetParamTypes(func);
			Mono.Cecil.TypeReference[] param_types = new Mono.Cecil.TypeReference[tmp_param_types.Length + 1];
            if (decl_type.IsValueType)
                param_types[0] = decl_type.MakeByReferenceType();
            else
                param_types[0] = decl_type;
            tmp_param_types.CopyTo(param_types, 1);
            MethodAttributes attrs = MethodAttributes.Public | MethodAttributes.Static;
            //определяем саму процедуру/функцию
            Mono.Cecil.MethodDefinition methb = null;
            methb = new Mono.Cecil.MethodDefinition(func.name, attrs, ret_type);
            tb.Methods.Add(methb);
            MethInfo mi = null;
            if (smi.Count != 0)
                mi = helper.AddMethod(func, methb, smi.Peek());
            else
                mi = helper.AddMethod(func, methb);
            mi.num_scope = num_scope;
            mi.disp = frm;
            mi.is_in_class = true;//процедура вложена в метод
            smi.Push(mi);
            Mono.Cecil.ParameterDefinition pb = null;
            int num = 0;
            Mono.Cecil.Cil.ILProcessor tmp_il = il;
            il = methb.Body.GetILProcessor();
            //if (ret_type != typeof(void)) mi.ret_val = il.DeclareLocal(ret_type);
            mi.nested = true;
            methb.Parameters.Add(
                new Mono.Cecil.ParameterDefinition("$obj$", ParameterAttributes.None, param_types[0])
            );
			methb.Parameters.Add(
				new Mono.Cecil.ParameterDefinition("$up$", ParameterAttributes.None, param_types[1])
			);
            num = 2;
            IParameterNode[] parameters = func.parameters;
            //
            Mono.Cecil.FieldDefinition[] fba = new Mono.Cecil.FieldDefinition[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                pb = new Mono.Cecil.ParameterDefinition(parameters[i].name, ParameterAttributes.None, param_types[i + num]);
                methb.Parameters.Add(pb);
                if (parameters[i].is_params)
                {
                    var customAttr = new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.ParamArrayAttributeCtor), new byte[] { 0x1, 0x0, 0x0, 0x0 });
                    pb.CustomAttributes.Add(customAttr);
                }                   
                if (func.functions_nodes.Length > 0)
                {
                    Mono.Cecil.FieldDefinition fb = null;
                    if (parameters[i].parameter_type == parameter_type.value)
                    {
                        fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, param_types[i + num]);
                        frm.tb.Fields.Add(fb);
                    }                       
                    else
                    {
                        Mono.Cecil.TypeReference pt = param_types[i + num].Module.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                        if (pt == null) mb.GetType(param_types[i + num].FullName.Substring(0, param_types[i + num].FullName.IndexOf('&')) + "*");
                        fb = new Mono.Cecil.FieldDefinition(parameters[i].name, FieldAttributes.Public, pt);
                        frm.tb.Fields.Add(fb);
                    }
                    helper.AddGlobalParameter(parameters[i], fb).meth = smi.Peek();
                    fba[i] = fb;
                }
                else helper.AddParameter(parameters[i], pb).meth = smi.Peek();
            }

            Mono.Cecil.Cil.VariableDefinition frame = new Mono.Cecil.Cil.VariableDefinition(cur_type);
            il.Body.Variables.Add(frame);
            mi.frame = frame;
            if (doc != null) il.Body.Method.DebugInformation.Scope.Variables.Add(new Mono.Cecil.Cil.VariableDebugInformation(frame, "$disp$")); ;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, frm.cb);
            il.Emit(OpCodes.Stloc, frame);

            //инициализация полей записи активации нелокальными параметрами
            if (func.functions_nodes.Length > 0)
                for (int j = 0; j < fba.Length; j++)
                {
                    il.Emit(OpCodes.Ldloc_0);
                    if (parameters[j].parameter_type == parameter_type.value)
                    {
                        il.Emit(OpCodes.Ldarg_S, (byte)(j + 2));
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarga_S, (byte)(j + 2));
                    }
                    il.Emit(OpCodes.Stfld, fba[j]);
                }
            funcs.Add(func);
            Mono.Cecil.MethodDefinition tmp = cur_meth;
            cur_meth = methb;
            //переводим переменные как нелокальные
            //non_local_variables[func] = new Tuple<MethodBuilder, MethodBuilder, List<ICommonFunctionNode>>(frm.mb, methb, new List<ICommonFunctionNode>(funcs));
            ConvertNonLocalVariables(func.var_definition_nodes, frm.mb);
            //переводим описания вложенных процедур
            ConvertNestedInMethodFunctionHeaders(func.functions_nodes, decl_type);
            foreach (ICommonNestedInFunctionFunctionNode f in func.functions_nodes)
                ConvertFunctionBody(f);
            if (frm != null)
                frm.mb.Body.GetILProcessor().Emit(OpCodes.Ret);

            cur_type = tmp_type;
            num_scope--;
            smi.Pop();
            funcs.RemoveAt(funcs.Count - 1);
        }

        private Mono.Cecil.TypeReference[] GetParamTypes(ICommonMethodNode func)
        {
            Mono.Cecil.TypeReference[] tt = null;
            int num = 0;
            IParameterNode[] parameters = func.parameters;
            tt = new Mono.Cecil.TypeReference[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Mono.Cecil.TypeReference tp = helper.GetTypeReference(parameters[i].type).tp;
                if (parameters[i].parameter_type == parameter_type.value)
                    tt[i + num] = tp;
                else
                {
                    tt[i + num] = tp.MakeByReferenceType();
                }
            }
            return tt;
        }

        private Mono.Cecil.TypeReference[] GetParamTypes(ICommonPropertyNode func)
        {
			Mono.Cecil.TypeReference[] tt = null;
            int num = 0;
            IParameterNode[] parameters = func.parameters;
            tt = new Mono.Cecil.TypeReference[parameters.Length];
            for (int i = 0; i < func.parameters.Length; i++)
            {
				Mono.Cecil.TypeReference tp = helper.GetTypeReference(parameters[i].type).tp;
                if (func.parameters[i].parameter_type == parameter_type.value)
                    tt[i + num] = tp;
                else
                {
                    tt[i + num] = tp.MakeByReferenceType();
                }
            }
            return tt;
        }

        //получение атрибутов метода
        private MethodAttributes GetMethodAttributes(SemanticTree.ICommonMethodNode value, bool is_accessor)
        {
            MethodAttributes attrs = ConvertFALToMethodAttributes(value.field_access_level);
            if (is_accessor)
                attrs = MethodAttributes.Public;
            if (value.overrided_method != null && value.name.IndexOf(".") != -1)
                attrs = MethodAttributes.Private;
            switch (value.polymorphic_state)
            {
                case polymorphic_state.ps_static: attrs |= MethodAttributes.Static; break;
                case polymorphic_state.ps_virtual:
                    attrs |= MethodAttributes.Virtual;
                    break;
                //ssyy
                case polymorphic_state.ps_virtual_abstract: attrs |= MethodAttributes.Virtual | MethodAttributes.Abstract; break;
                //\ssyy
            }
            return attrs;
        }

        private MethodAttributes GetConstructorAttributes(SemanticTree.ICommonMethodNode value)
        {
            MethodAttributes attrs = ConvertFALToMethodAttributes(value.field_access_level);
            switch (value.polymorphic_state)
            {
                case polymorphic_state.ps_virtual: attrs |= MethodAttributes.Virtual; break;
            }
            return attrs;
        }

        //перевод заголовка конструктора
        private void ConvertConstructorHeader(SemanticTree.ICommonMethodNode value)
        {
            if (helper.GetConstructor(value) != null) return;

			//определяем конструктор
			Mono.Cecil.MethodDefinition cnstr;
            IRuntimeManagedMethodBody irmmb = null;
            if (value.polymorphic_state == polymorphic_state.ps_static)
            {
                cnstr = new Mono.Cecil.MethodDefinition(".cctor", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                cur_type.Methods.Add(cnstr);
                cur_ti.static_cnstr = cnstr;
            }
            else
            {
                Mono.Cecil.TypeReference[] param_types = GetParamTypes(value);
                MethodAttributes attrs = GetConstructorAttributes(value);

                irmmb = value.function_code as IRuntimeManagedMethodBody;
                if (irmmb != null)
                {
                    if (irmmb.runtime_statement_type == SemanticTree.runtime_statement_type.ctor_delegate)
                    {
                        attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
                        param_types = new Mono.Cecil.TypeReference[2];
                        param_types[0] = mb.TypeSystem.Object;
                        param_types[1] = mb.TypeSystem.IntPtr;
                    }
                }
                cnstr = new Mono.Cecil.MethodDefinition(".ctor", attrs | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mb.TypeSystem.Void);
                
                for(var i = 0; i < param_types.Length; i++)
                {
                    cnstr.Parameters.Add(new Mono.Cecil.ParameterDefinition(param_types[i]));
                }

                cnstr.HasThis = true;
                cur_type.Methods.Add(cnstr);
            }

            if (irmmb != null)
            {
                cnstr.IsRuntime = true;
            }

            MethInfo mi = null;
            mi = helper.AddConstructor(value, cnstr);
            mi.num_scope = num_scope + 1;
            if (save_debug_info)
            {
                if (value.function_code is IStatementsListNode)
                    MarkSequencePoint(cnstr.Body.GetILProcessor(), ((IStatementsListNode)value.function_code).LeftLogicalBracketLocation);
            }
            ConvertConstructorParameters(value, cnstr);
        }

        //процедура проверки нужно ли заменять тип возвр. знач. метода get_val массива на указатель
        private bool IsNeedCorrectGetType(TypeInfo cur_ti, Mono.Cecil.TypeReference ret_type)
        {
            return (cur_ti.is_arr && ret_type.FullName != mb.TypeSystem.Void.FullName && ret_type.IsValueType && !TypeFactory.IsStandType(ret_type) && !ret_type.Resolve().IsEnum);
        }

        private bool IsPropertyAccessor(ICommonMethodNode value)
        {
            return comp_opt.target == TargetType.Dll && prop_accessors.ContainsKey(value);
        }

        private void ConvertExternalMethod(SemanticTree.ICommonMethodNode meth)
        {
            IStatementsListNode sl = (IStatementsListNode)meth.function_code;
            IStatementNode[] statements = sl.statements;
            //функция импортируется из dll
            Mono.Cecil.TypeReference ret_type = null;
            //получаем тип возвр. значения
            if (meth.return_value_type == null)
                ret_type = null;//typeof(void);
            else
                ret_type = helper.GetTypeReference(meth.return_value_type).tp;
            Mono.Cecil.TypeReference[] param_types = GetParamTypes(meth);//получаем параметры процедуры
            Mono.Cecil.MethodDefinition methb = null;
            if (statements[0] is IExternalStatementNode)
            {
                IExternalStatementNode esn = (IExternalStatementNode)statements[0];
                string module_name = Tools.ReplaceAllKeys(esn.module_name, StandartDirectories);
                methb = new Mono.Cecil.MethodDefinition(meth.name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig, ret_type);
                cur_type.Methods.Add(methb);
                var moduleRef = new Mono.Cecil.ModuleReference(module_name);
                mb.ModuleReferences.Add(moduleRef);
                methb.PInvokeInfo = new Mono.Cecil.PInvokeInfo(PInvokeAttributes.CallConvWinapi, esn.name, moduleRef);
                
                foreach (var paramType in param_types)
                    methb.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
            }
            else
            {
                methb = new Mono.Cecil.MethodDefinition(meth.name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl | MethodAttributes.HideBySig, ret_type);
                cur_type.Methods.Add(methb);

				foreach (var paramType in param_types)
					methb.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));

                methb.IsPreserveSig = true;
            }
			methb.IsPreserveSig = true;
			helper.AddMethod(meth, methb);
            IParameterNode[] parameters = meth.parameters;
            //определяем параметры с указанием имени
            for (int j = 0; j < parameters.Length; j++)
            {
                ParameterAttributes pars = ParameterAttributes.None;
                //if (func.parameters[j].parameter_type == parameter_type.var)
                //  pars = ParameterAttributes.Out;
                methb.Parameters[j].Name = parameters[j].name;
                methb.Parameters[j].Attributes = pars;
            }
        }

        //перевод заголовка метода
        private void ConvertMethodHeader(SemanticTree.ICommonMethodNode value)
        {
            if (value.is_constructor == true)
            {
                ConvertConstructorHeader(value);
                return;
            }

            if (helper.GetMethod(value) != null)
                return;
            if (value.function_code is IStatementsListNode)
            {
                IStatementsListNode sl = (IStatementsListNode)value.function_code;
                IStatementNode[] statements = sl.statements;
                if (statements.Length > 0 && (statements[0] is IExternalStatementNode || statements[0] is IPInvokeStatementNode))
                {
                    ConvertExternalMethod(value);
                    return;
                }
            }
            Mono.Cecil.MethodDefinition methb = null;
            bool is_prop_acc = IsPropertyAccessor(value);
            MethodAttributes attrs = GetMethodAttributes(value, is_prop_acc);
            IRuntimeManagedMethodBody irmmb = value.function_code as IRuntimeManagedMethodBody;
            if (irmmb != null)
            {
                if ((irmmb.runtime_statement_type == SemanticTree.runtime_statement_type.invoke_delegate) ||
                    (irmmb.runtime_statement_type == SemanticTree.runtime_statement_type.begin_invoke_delegate) ||
                    (irmmb.runtime_statement_type == SemanticTree.runtime_statement_type.end_invoke_delegate))
                {
                    attrs = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                        MethodAttributes.Virtual;
                }
            }

            //определяем метод
            string method_name = OperatorsNameConvertor.convert_name(value.name);

            if (method_name != null)
            {
                attrs |= MethodAttributes.SpecialName;
            }
            else
            {
                bool get_set = false;
                method_name = GetPossibleAccessorName(value, out get_set);
                if (get_set)
                {
                    attrs |= MethodAttributes.SpecialName;
                }
            }

            //ssyy
            if (value.comperehensive_type.IsInterface)
            {
                attrs |= MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.NewSlot;
            }

            if (value.is_final)
            {
                
                attrs |= MethodAttributes.Virtual | MethodAttributes.Final;
            }

            if (value.newslot_awaited)
            {
                attrs |= MethodAttributes.NewSlot;
            }
            //\ssyy
            if (value.name == "op_Implicit" || value.name == "op_Explicit" || value.name == "op_Equality" || value.name == "op_Inequality")
                attrs |= MethodAttributes.HideBySig | MethodAttributes.SpecialName;
            methb = new Mono.Cecil.MethodDefinition(method_name, attrs, mb.TypeSystem.Void);
            cur_type.Methods.Add(methb);

            if (value.is_generic_function)
            {
                int count = value.generic_params.Count;
                string[] names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    names[i] = value.generic_params[i].name;
                }

                for (var i = 0; i < count; i++)
                {
                    methb.GenericParameters.Add(
                        new Mono.Cecil.GenericParameter(names[i], methb)
                    );
                }

                Mono.Cecil.TypeReference[] genargs = methb.GenericParameters.ToArray();
                for (int i = 0; i < count; i++)
                {
                    helper.AddExistingType(value.generic_params[i], genargs[i]);
                }
                foreach (ICommonTypeNode par in value.generic_params)
                {
                    converting_generic_param = par;
                    ConvertTypeHeaderInSpecialOrder(par);
                }
                ConvertTypeInstancesInFunction(value);
            }

            Mono.Cecil.TypeReference ret_type = null;
            bool is_ptr_ret_type = false;
            if (value.return_value_type == null)
                ret_type = mb.TypeSystem.Void;
            else
            {
                TypeInfo ti = helper.GetTypeReference(value.return_value_type);
                if (ti == null && value.return_value_type.name == null)//not used lambda, ignore
                    ret_type = mb.TypeSystem.Void;
                else
                    ret_type = ti.tp;
                if (IsNeedCorrectGetType(cur_ti, ret_type))
                {
                    ret_type = ret_type.MakePointerType();
                    is_ptr_ret_type = true;
                }
            }
            Mono.Cecil.TypeReference[] param_types = GetParamTypes(value);

            for (var i = 0; i < param_types.Length; i++)
            {
                methb.Parameters.Add(
                    new Mono.Cecil.ParameterDefinition(param_types[i])
                );
            }

            methb.ReturnType = ret_type;

            if (irmmb != null)
            {
                methb.ImplAttributes = Mono.Cecil.MethodImplAttributes.Runtime;
			}

            if (save_debug_info)
            {
                if (value.function_code is IStatementsListNode)
                    MarkSequencePoint(methb.Body.GetILProcessor(), ((IStatementsListNode)value.function_code).LeftLogicalBracketLocation);
            }
            MethInfo mi = null;
            mi = helper.AddMethod(value, methb);
            //binding CloneSet to set type
            if (value.comperehensive_type.type_special_kind == type_special_kind.base_set_type && value.name == "GetEnumerator")
            {
                helper.GetTypeReference(value.comperehensive_type).enumerator_meth = methb;
            }
            mi.is_ptr_ret_type = is_ptr_ret_type;
            mi.num_scope = num_scope + 1;
            ConvertMethodParameters(value, methb);
        }

        private void ConvertMethodParameters(ICommonMethodNode value, Mono.Cecil.MethodDefinition methb)
        {
            Mono.Cecil.ParameterDefinition pb = null;
            IParameterNode[] parameters = value.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                object default_value = null;
                if (parameters[i].default_value != null)
                    default_value = helper.GetConstantForExpression(parameters[i].default_value);

                ParameterAttributes pa = ParameterAttributes.None;
                if (default_value != null)
                    pa |= ParameterAttributes.Optional;
                pb = methb.Parameters[i];
                pb.Name = parameters[i].name;
                pb.Attributes = pa;
                if (default_value != null)
                    if (default_value is TreeRealization.null_const_node) // SSM 20/04/21
                        pb.Constant = null;
                    else pb.Constant = default_value;
                helper.AddParameter(parameters[i], pb);
                if (parameters[i].is_params)
                    pb.CustomAttributes.Add(
                        new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.ParamArrayAttributeCtor), new byte[] { 0x1, 0x0, 0x0, 0x0 })
                    );
            }
        }

        private void ConvertConstructorParameters(ICommonMethodNode value, Mono.Cecil.MethodDefinition methb)
        {
            Mono.Cecil.ParameterDefinition pb = null;
            IParameterNode[] parameters = value.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                object default_value = null;
                if (parameters[i].default_value != null)
                    default_value = helper.GetConstantForExpression(parameters[i].default_value);
                ParameterAttributes pa = ParameterAttributes.None;
                if (parameters[i].parameter_type == parameter_type.var)
                    pa = ParameterAttributes.Retval;

                if (default_value != null)
                    pa |= ParameterAttributes.Optional;
                pb = methb.Parameters[i];
                pb.Name = parameters[i].name;
                pb.Attributes = pa;
				if (default_value != null)
                {
                    pb.HasDefault = true;
                    if (default_value is TreeRealization.null_const_node) // SSM 20/04/21
                        pb.Constant = null;
                    else pb.Constant = default_value;
                }
                    
                helper.AddParameter(parameters[i], pb);
                if (parameters[i].is_params)
					pb.CustomAttributes.Add(
						new Mono.Cecil.CustomAttribute(mb.ImportReference(TypeFactory.ParamArrayAttributeCtor), new byte[] { 0x1, 0x0, 0x0, 0x0 })
					);
			}
        }

        //вызов откомпилированного статического метода
        public override void visit(SemanticTree.ICompiledStaticMethodCallNode value)
        {
            if (comp_opt.dbg_attrs == DebugAttributes.Release && has_debug_conditional_attr(mb.ImportReference(value.static_method.method_info)))
                return;
            bool tmp_dot = is_dot_expr;//идет ли после этого точка
            is_dot_expr = false;
            Mono.Cecil.ParameterReference[] pinfs = mb.ImportReference(value.static_method.method_info).Parameters.ToArray();
            //кладем параметры
            Mono.Cecil.MethodReference mi = mb.ImportReference(value.static_method.method_info);
            IExpressionNode[] real_parameters = value.real_parameters;
            IParameterNode[] parameters = value.static_method.parameters;
            if (mi.DeclaringType.FullName == TypeFactory.ArrayType.FullName && mi.Name == "Resize" && helper.GetTypeReference(value.template_parametres[0]).tp.IsPointer)
            {
                is_addr = true;
                real_parameters[0].visit(this);
                is_addr = false;
                Instruction l1 = il.Create(OpCodes.Nop);
                Instruction l2 = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Beq, l1);
                il.Emit(OpCodes.Dup);
                //il.Emit(OpCodes.Ldloc, lb);
                il.Emit(OpCodes.Ldind_Ref);
                real_parameters[1].visit(this);
                Mono.Cecil.TypeReference el_tp = helper.GetTypeReference(value.template_parametres[0]).tp;
                il.Emit(OpCodes.Newarr, el_tp);
                Mono.Cecil.Cil.VariableDefinition tmp_lb = new Mono.Cecil.Cil.VariableDefinition(el_tp.MakeArrayType());
                il.Body.Variables.Add(tmp_lb);
                il.Emit(OpCodes.Stloc, tmp_lb);
                il.Emit(OpCodes.Ldloc, tmp_lb);
                real_parameters[0].visit(this);
                il.Emit(OpCodes.Callvirt, mb.ImportReference(TypeFactory.ArrayLengthGetMethod));
                real_parameters[1].visit(this);
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.MathMinMethod));
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ArrayCopyMethod));
                il.Emit(OpCodes.Ldloc, tmp_lb);
                il.Emit(OpCodes.Br, l2);
                il.Append(l1);
                real_parameters[1].visit(this);
                il.Emit(OpCodes.Newarr, el_tp);
                il.Append(l2);
                il.Emit(OpCodes.Stind_Ref);
                TypeInfo ti = helper.GetTypeReference(real_parameters[0].type.element_type);
                this.CreateInitCodeForUnsizedArray(il, ti, real_parameters[0], real_parameters[1]);
                return;
            }
            Mono.Cecil.Cil.VariableDefinition len_lb = null;
            Mono.Cecil.Cil.VariableDefinition start_index_lb = null;
            if (mi.DeclaringType.FullName == TypeFactory.ArrayType.FullName && mi.Name == "Resize" && real_parameters.Length == 2)
            {
                start_index_lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                il.Body.Variables.Add(start_index_lb);
				il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, start_index_lb);
                Instruction lbl = il.Create(OpCodes.Nop);
                real_parameters[0].visit(this);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Beq, lbl);
                real_parameters[0].visit(this);
                il.Emit(OpCodes.Ldlen);
                //real_parameters[1].visit(this);
                //il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Stloc, start_index_lb);
                il.Append(lbl);
            }
            //len_lb = EmitArguments(parameters, real_parameters, mi);
            
            for (int i = 0; i < real_parameters.Length; i++)
            {
            	if (real_parameters[i] is INullConstantNode && parameters[i].type.is_nullable_type)
                {
        			Mono.Cecil.TypeReference tp = helper.GetTypeReference(parameters[i].type).tp;
        			Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
                    il.Body.Variables.Add(lb);
        			il.Emit(OpCodes.Ldloca, lb);
        			il.Emit(OpCodes.Initobj, tp);
        			il.Emit(OpCodes.Ldloc, lb);
        			continue;
        		}
                if (parameters[i].parameter_type == parameter_type.var)
                    is_addr = true;
                //TODO:Переделать.
                if (!is_addr)
                {
                    if (pinfs[i].ParameterType.IsByReference)
                    {
                        is_addr = true;
                    }
                }
                if (parameters[i].type is ICompiledTypeNode && (parameters[i].type as ICompiledTypeNode).compiled_type.FullName == mb.TypeSystem.Char.FullName && parameters[i].parameter_type == parameter_type.var
                    && real_parameters[i] is ISimpleArrayIndexingNode && helper.GetTypeReference((real_parameters[i] as ISimpleArrayIndexingNode).array.type).tp.FullName == mb.TypeSystem.String.FullName)
                {
                    copy_string = true;
                }
                real_parameters[i].visit(this);
                
                if (mi.DeclaringType.FullName == TypeFactory.ArrayType.FullName && mi.Name == "Resize" && i == 1)
                {
                    if (real_parameters.Length == 2)
                    {
                        len_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(real_parameters[1].type).tp);
                        il.Body.Variables.Add(len_lb);
                        il.Emit(OpCodes.Stloc, len_lb);
                        il.Emit(OpCodes.Ldloc, len_lb);
                    }
                }
                //ICompiledTypeNode ctn = value.real_parameters[i].type as ICompiledTypeNode;
                ICompiledTypeNode ctn2 = parameters[i].type as ICompiledTypeNode;
                ITypeNode ctn3 = real_parameters[i].type;
                ITypeNode ctn4 = real_parameters[i].conversion_type;
                if (ctn2 != null && !(real_parameters[i] is SemanticTree.INullConstantNode) && (ctn3.is_value_type || ctn3.is_generic_parameter) && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
                    il.Emit(OpCodes.Box, helper.GetTypeReference(ctn3).tp);
                else if (ctn2 != null && !(real_parameters[i] is SemanticTree.INullConstantNode) && ctn4 != null && (ctn4.is_value_type || ctn4.is_generic_parameter) && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
                    il.Emit(OpCodes.Box, helper.GetTypeReference(ctn4).tp);
                is_addr = false;
            }
            //вызов метода

            if (value.template_parametres.Length > 0)
            {
                Mono.Cecil.TypeReference[] type_arr = new Mono.Cecil.TypeReference[value.template_parametres.Length];
                for (int int_i = 0; int_i < value.template_parametres.Length; int_i++)
                {
                    type_arr[int_i] = helper.GetTypeReference(value.template_parametres[int_i]).tp;
                }
                mi = new Mono.Cecil.GenericInstanceMethod(mi);
                foreach (var genArgType in type_arr)
                    ((Mono.Cecil.GenericInstanceMethod)mi).GenericArguments.Add(genArgType);
            }
            il.Emit(OpCodes.Call, mi);
            if (tmp_dot)
            {
                //MethodInfo mi = value.static_method.method_info;
                if ((mi.ReturnType.IsValueType || mi.ReturnType.IsGenericParameter) && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mi.ReturnType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
                is_dot_expr = tmp_dot;
            }
            if (mi.DeclaringType.FullName == TypeFactory.ArrayType.FullName && mi.Name == "Resize")
            {
                if (real_parameters.Length == 2)
                {
                    this.CreateInitCodeForUnsizedArray(il, real_parameters[0].type.element_type,
                        real_parameters[0], len_lb, start_index_lb);
                }
            }
            EmitFreePinnedVariables();
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
        }

        private bool has_debug_conditional_attr(Mono.Cecil.MethodReference mi)
        {
            // не уверен, что учитываются атрибуты из предков
            var attr = mi.Resolve().CustomAttributes
                .FirstOrDefault(item => item.AttributeType.FullName == "System.Diagnostics.ConditionalAttribute");

            if (attr == null)
                return false;

            var attrArg = (string)attr.ConstructorArguments[0].Value;

            return (attrArg == "DEBUG");
        }

        private bool has_debug_conditional_attr(IAttributeNode[] attrs)
        {
            foreach (IAttributeNode attr in attrs)
            {
                if (attr.AttributeType is ICompiledTypeNode && (attr.AttributeType as ICompiledTypeNode).compiled_type.FullName == "System.Diagnostics.ConditionalAttribute")
                {
                    if ((string)attr.Arguments[0].value == "DEBUG")
                        return true;
                    return false;
                }
            }
            return false;
        }

        //вызов откомпилированного метода
        public override void visit(SemanticTree.ICompiledMethodCallNode value)
        {
            IExpressionNode[] real_parameters = value.real_parameters;
            IParameterNode[] parameters = value.compiled_method.parameters;
            bool tmp_dot = is_dot_expr;
            is_dot_expr = true;
            //DarkStar Fixed: type t:=i.gettype();
            bool _box = value.obj.type.is_value_type && !value.compiled_method.method_info.DeclaringType.IsValueType;
            if (!_box && value.obj.conversion_type != null)
            	_box = value.obj.conversion_type.is_value_type;
            if (_box)
                is_dot_expr = false;
            if ((value.compiled_method.polymorphic_state == polymorphic_state.ps_virtual || value.compiled_method.polymorphic_state == polymorphic_state.ps_virtual_abstract || value.compiled_method.polymorphic_state == polymorphic_state.ps_common) && (value.obj is ICommonParameterReferenceNode || value.obj is ICommonClassFieldReferenceNode))
                virtual_method_call = true;
            value.obj.visit(this);
            virtual_method_call = false;
            if (value.obj.type.is_value_type && !value.compiled_method.method_info.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(value.obj.type).tp);
            }
            else if (value.obj.type.is_generic_parameter && !(value.obj is IAddressedExpressionNode))
            {
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.obj.type).tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
            else if (value.obj.conversion_type != null && value.obj.conversion_type.is_value_type && !value.compiled_method.method_info.DeclaringType.IsValueType)
            {
            	il.Emit(OpCodes.Box, helper.GetTypeReference(value.obj.conversion_type).tp);
            }
            else if (_box && value.obj.type.is_value_type)
            {
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.obj.type).tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
            is_dot_expr = false;
            EmitArguments(parameters, real_parameters);
            Mono.Cecil.MethodReference mi = mb.ImportReference(value.compiled_method.method_info);
            if (value.compiled_method.comperehensive_type.is_value_type || 
                //value.compiled_method.comperehensive_type is ICompiledTypeNode && (value.compiled_method.comperehensive_type as ICompiledTypeNode).compiled_type == TypeFactory.EnumType || 
                !value.virtual_call && value.compiled_method.polymorphic_state == polymorphic_state.ps_virtual || 
                value.compiled_method.polymorphic_state == polymorphic_state.ps_static)
            {
                il.Emit(OpCodes.Call, mi);
            }
            else
            {
                if (value.obj.type.is_generic_parameter)
                    il.Emit(OpCodes.Constrained, helper.GetTypeReference(value.obj.type).tp);
                else if (value.obj.conversion_type != null && value.obj.conversion_type.is_generic_parameter)
                    il.Emit(OpCodes.Constrained, helper.GetTypeReference(value.obj.conversion_type).tp);
                il.Emit(OpCodes.Callvirt, mi);
            }

            EmitFreePinnedVariables();
            if (tmp_dot)
            {
                //MethodInfo mi = value.compiled_method.method_info;
                if ((mi.ReturnType.IsValueType || mi.ReturnType.IsGenericParameter) && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mi.ReturnType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
            }
            else
            {
                is_dot_expr = false;
            }
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
        }

        //вызов статического метода
        public override void visit(SemanticTree.ICommonStaticMethodCallNode value)
        {
            //if (save_debug_info)
            //MarkSequencePoint(value.Location);
            IExpressionNode[] real_parameters = value.real_parameters;
            MethInfo meth = helper.GetMethod(value.static_method);
            Mono.Cecil.MethodReference mi = meth.mi;
            bool tmp_dot = is_dot_expr;
            is_dot_expr = false;
            bool is_comp_gen = false;
            IParameterNode[] parameters = value.static_method.parameters;
            EmitArguments(parameters, real_parameters);
            il.Emit(OpCodes.Call, mi);
            if (tmp_dot)
            {
                if (value.type.is_value_type && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.type).tp);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
                is_dot_expr = tmp_dot;
            }
            else if (meth.is_ptr_ret_type && is_addr == false) il.Emit(OpCodes.Ldobj, helper.GetTypeReference(value.static_method.return_value_type).tp);
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
        }

        private Hashtable mis = new Hashtable();

        private void AddToCompilerGenerated(Mono.Cecil.MethodReference mi)
        {
            mis[mi] = mi;
        }

        private bool IsArrayGetter(Mono.Cecil.MethodReference mi)
        {
            if (mis[mi] != null) return true;
            return false;
        }

        private bool CheckForCompilerGenerated(IExpressionNode expr)
        {
            if (save_debug_info)
                if (expr is ICommonMethodCallNode)
                {
                    ICommonMethodCallNode cmcn = expr as ICommonMethodCallNode;
                    if (IsArrayGetter(helper.GetMethod(cmcn.method).mi))
                    {
                        // MarkSequencePoint(il, 0xFeeFee, 1, 0xFeeFee, 1);
                        return true;
                    }
                    return false;
                }
            return false;
        }

        bool virtual_method_call = false;

        //вызов нестатического метода
        public override void visit(SemanticTree.ICommonMethodCallNode value)
        {
            MethInfo meth = helper.GetMethod(value.method);
            Mono.Cecil.MethodReference mi = meth.mi;
            IExpressionNode[] real_parameters = value.real_parameters;
            bool tmp_dot = is_dot_expr;
            if (!tmp_dot)
                is_dot_expr = true;
            if ((value.method.polymorphic_state == polymorphic_state.ps_virtual || value.method.polymorphic_state == polymorphic_state.ps_virtual_abstract || value.method.polymorphic_state == polymorphic_state.ps_common) && (value.obj is ICommonParameterReferenceNode || value.obj is ICommonClassFieldReferenceNode))
                virtual_method_call = true;
            value.obj.visit(this);
            virtual_method_call = false;
            if ((value.obj.type.is_value_type) && !value.method.comperehensive_type.is_value_type)
            {
                if (!(value.obj is ICommonParameterReferenceNode && must_push_addr))
                    il.Emit(OpCodes.Box, helper.GetTypeReference(value.obj.type).tp);
            }
            else if (value.obj.type.is_generic_parameter && !(value.obj is IAddressedExpressionNode))
            {
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.obj.type).tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
            else if (value.obj.conversion_type != null && value.obj.conversion_type.is_value_type && !value.method.comperehensive_type.is_value_type)
            {
            	il.Emit(OpCodes.Box, helper.GetTypeReference(value.obj.conversion_type).tp);
            }
            else if (value.obj.type.is_value_type && !(value.obj is IAddressedExpressionNode) && !(value.obj is IThisNode) 
                && !(value.obj is ICommonMethodCallNode) && !(value.obj is ICommonStaticMethodCallNode) 
                && !(value.obj is ICommonConstructorCall) && !(value.obj is ICommonNamespaceFunctionCallNode) 
                && !(value.obj is ICommonNestedInFunctionFunctionCallNode)
                && !(value.obj is IQuestionColonExpressionNode)
                && !(value.obj is IDoubleQuestionColonExpressionNode)
                && !(value.obj.conversion_type != null && !value.obj.conversion_type.is_value_type))
            {
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.obj.type).tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
                il.Emit(OpCodes.Ldloca, lb);
            }
            is_dot_expr = false;
            //bool is_comp_gen = false;
            //bool need_fee = false;
            IParameterNode[] parameters = value.method.parameters;
            EmitArguments(parameters, real_parameters);
            //вызов метода
            //(ssyy) Функции размерных типов всегда вызываются через call
            if (value.method.comperehensive_type.is_value_type || !value.virtual_call && value.method.polymorphic_state == polymorphic_state.ps_virtual || value.method.polymorphic_state == polymorphic_state.ps_static /*|| !value.virtual_call || (value.method.polymorphic_state != polymorphic_state.ps_virtual && value.method.polymorphic_state != polymorphic_state.ps_virtual_abstract && !value.method.common_comprehensive_type.IsInterface)*/)
            {
                il.Emit(OpCodes.Call, mi);
            }
            else
            {
                if (value.obj.type.is_generic_parameter)
                    il.Emit(OpCodes.Constrained, helper.GetTypeReference(value.obj.type).tp);
                else if (value.obj.conversion_type != null && value.obj.conversion_type.is_generic_parameter && (!value.obj.type.IsInterface || value.obj.conversion_type.ImplementingInterfaces.Contains(value.obj.type)))
                    il.Emit(OpCodes.Constrained, helper.GetTypeReference(value.obj.conversion_type).tp);
                il.Emit(OpCodes.Callvirt, mi);
            }
            EmitFreePinnedVariables();
            if (tmp_dot == true)
            {
                //if (mi.ReturnType.IsValueType && !NETGeneratorTools.IsPointer(mi.ReturnType))
                //Для правильной работы шаблонов поменял условие (ssyy, 15.05.2009)
                if ((value.method.return_value_type != null && value.method.return_value_type.is_value_type /*|| value.method.return_value_type != null && value.method.return_value_type.is_generic_parameter*/) && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = (mi.ReturnType.HasGenericParameters || mi.ReturnType.IsGenericParameter) ?
                        new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.method.return_value_type).tp) :
						new Mono.Cecil.Cil.VariableDefinition(mi.ReturnType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
            }
            else
            {
                is_dot_expr = false;
                if (meth.is_ptr_ret_type && is_addr == false) il.Emit(OpCodes.Ldobj, helper.GetTypeReference(value.method.return_value_type).tp);
            }
            if (value.last_result_function_call)
            {
                il.Emit(OpCodes.Ret);
            }
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
        }

        bool CallCloneIfNeed(Mono.Cecil.Cil.ILProcessor il, IParameterNode parameter, IExpressionNode expr)
        {
            TypeInfo ti = helper.GetTypeReference(parameter.type);
            if (ti != null && ti.clone_meth != null && parameter.parameter_type == parameter_type.value && !parameter.is_const &&
                parameter.type.type_special_kind != type_special_kind.base_set_type)
            {
                il.Emit(OpCodes.Call, ti.clone_meth);
                return true;
            }
            return false;
        }

        //вызов вложенной процедуры
        public override void visit(SemanticTree.ICommonNestedInFunctionFunctionCallNode value)
        {
            IExpressionNode[] real_parameters = value.real_parameters;
            //if (save_debug_info)
            //MarkSequencePoint(value.Location);

            MethInfo meth = helper.GetMethod(value.common_function);
            Mono.Cecil.MethodReference mi = meth.mi;
            bool tmp_dot = is_dot_expr;
            is_dot_expr = false;
            MethInfo cur_mi = null;
            int scope_off = 0;
            //если процедура вложена в метод, то кладем дополнительный параметр this
            if (meth.is_in_class == true)
                il.Emit(OpCodes.Ldarg_0);
            if (smi.Count > 0)
            {
                cur_mi = smi.Peek();
                scope_off = meth.num_scope - cur_mi.num_scope;
            }
            //вызываемой процедуре нужно передать верхнюю по отношению
            // к ней запись активации. Можно вызвать процедуру уровня -1, 0, 1, ...
            //относительно вызывающей функции. Проходимся по цепочке записей активации
            //чтобы достучаться до нужной.
            if (meth.nested == true)
            {
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                if (scope_off <= 0)
                {
                    scope_off = Math.Abs(scope_off) + 1;
                    for (int j = 0; j < scope_off; j++)
                    {
                        il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                        cur_mi = cur_mi.up_meth;
                    }
                }
            }
            bool is_comp_gen = false;
            IParameterNode[] parameters = value.common_function.parameters;
            for (int i = 0; i < real_parameters.Length; i++)
            {
                if (parameters[i].parameter_type == parameter_type.var)
                    is_addr = true;
                ITypeNode ctn = real_parameters[i].type;
                TypeInfo ti = helper.GetTypeReference(ctn);
                
                //(ssyy) moved up
                ITypeNode tn2 = parameters[i].type;
                ICompiledTypeNode ctn2 = tn2 as ICompiledTypeNode;
                ITypeNode ctn3 = real_parameters[i].type;
                //(ssyy) 07.12.2007 При боксировке нужно вызывать Ldsfld вместо Ldsflda.
                //Дополнительная проверка введена именно для этого.
                bool box_awaited =
                    (ctn2 != null && ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || tn2.IsInterface) && !(real_parameters[i] is SemanticTree.INullConstantNode) && (ctn3.is_value_type || ctn3.is_generic_parameter);

                if (ti != null && ti.clone_meth != null && ti.tp != null && ti.tp.IsValueType && !box_awaited && !parameters[i].is_const)
                    is_dot_expr = true;
                is_comp_gen = CheckForCompilerGenerated(real_parameters[i]);
                real_parameters[i].visit(this);
                is_dot_expr = false;
                CallCloneIfNeed(il, parameters[i], real_parameters[i]);
                if (box_awaited)
                    il.Emit(OpCodes.Box, helper.GetTypeReference(ctn3).tp);
                is_addr = false;
            }
            //if (save_debug_info && need_fee)
            // MarkSequencePoint(il, value.Location);
            //вызов процедуры
            il.Emit(OpCodes.Call, mi);
            EmitFreePinnedVariables();
            if (tmp_dot == true)
            {
                if (mi.ReturnType.IsValueType && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mi.ReturnType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
                is_dot_expr = tmp_dot;
            }
            else
                if (meth.is_ptr_ret_type && is_addr == false) il.Emit(OpCodes.Ldobj, helper.GetTypeReference(value.common_function.return_value_type).tp);
            //if (is_stmt == true) il.Emit(OpCodes.Pop);
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
        }

        //это не очень нравится - у некоторых
        private bool GenerateStandardFuncCall(ICommonNamespaceFunctionCallNode value, Mono.Cecil.Cil.ILProcessor il)
        {
            IExpressionNode[] real_parameters = value.real_parameters;
            switch (value.namespace_function.SpecialFunctionKind)
            {
                case SpecialFunctionKind.NewArray:
                    //первый параметр - ITypeOfOperator
                    TypeInfo ti = helper.GetTypeReference(((ITypeOfOperator)real_parameters[0]).oftype);
                    int rank = (real_parameters[1] as IIntConstantNode).constant_value;

                    if (ti.tp.IsValueType && ti.init_meth != null || ti.is_arr || ti.is_set || ti.is_typed_file || ti.is_text_file || ti.tp.FullName == mb.TypeSystem.String.FullName)
                    {
                        //value.real_parameters[1].visit(this);
                        if (rank == 0)
                        {
                            CreateUnsizedArray(il, ti, real_parameters[1]);
                        }
                        else if (rank == 1)
                        {
                            real_parameters[2].visit(this);
                            Mono.Cecil.Cil.VariableDefinition size = NETGeneratorTools.CreateLocal(il, helper.GetTypeReference(real_parameters[2].type).tp);
                            CreateUnsizedArray(il, ti, size);
                            CreateInitCodeForUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, size);
                        }
                        else
                        {
                            if (real_parameters.Length <= 2 + rank)
                            {
                                CreateNDimUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, rank, real_parameters);
                                List<IExpressionNode> prms = new List<IExpressionNode>();
                                prms.AddRange(real_parameters);
                                prms.RemoveRange(0, 2);
                                CreateInitCodeForNDimUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, rank, prms.ToArray());
                            }
                        }
                    }
                    else
                    {
                        if (rank == 0)
                        {
                            CreateUnsizedArray(il, ti, real_parameters[1]);
                        }
                        else if (rank == 1)
                        {
                            CreateUnsizedArray(il, ti, real_parameters[2]);
                        }
                        else
                        {
                            if (real_parameters.Length <= 2 + rank)
                                CreateNDimUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, rank, real_parameters);
                        }
                    }
                    if (real_parameters.Length > 2 + rank)
                        if (rank == 1)
                            InitializeUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, real_parameters, rank);
                        else if (rank != 0)
                            InitializeNDimUnsizedArray(il, ti, ((ITypeOfOperator)real_parameters[0]).oftype, real_parameters, rank);
                    return true;
            }
            return false;
        }

        private MethInfo MakeStandardFunc(ICommonNamespaceFunctionCallNode value)
        {
            ICommonNamespaceFunctionNode func = value.namespace_function;
            Mono.Cecil.MethodDefinition methodb;
            Mono.Cecil.ParameterDefinition pb;
            Mono.Cecil.Cil.ILProcessor il;
            MethInfo mi;
            switch (func.SpecialFunctionKind)
            {
                case SpecialFunctionKind.New:
                    methodb = new Mono.Cecil.MethodDefinition(func.name, MethodAttributes.Public | MethodAttributes.Static, mb.TypeSystem.Void);
                    cur_type.Methods.Add(methodb);
                    methodb.Parameters.Add(new Mono.Cecil.ParameterDefinition("ptr", ParameterAttributes.None, mb.TypeSystem.Void.MakePointerType().MakeByReferenceType()));
                    methodb.Parameters.Add(new Mono.Cecil.ParameterDefinition("size", ParameterAttributes.None, mb.TypeSystem.Int32));
                    il = methodb.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.MarshalAllocHGlobalMethod));
                    il.Emit(OpCodes.Stind_I);
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Byte.MakeArrayType());
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Newarr, mb.TypeSystem.Byte);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloc, lb);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldind_I);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.MarshalCopyMethod));
                    il.Emit(OpCodes.Ret);
                    mi = helper.AddMethod(func, methodb);
                    mi.stand = true;
                    return mi;
                case SpecialFunctionKind.Dispose:
                    methodb = new Mono.Cecil.MethodDefinition(func.name, MethodAttributes.Public | MethodAttributes.Static, mb.TypeSystem.Void);
                    cur_type.Methods.Add(methodb);
                    methodb.Parameters.Add(new Mono.Cecil.ParameterDefinition("ptr", ParameterAttributes.None, mb.TypeSystem.Void.MakePointerType().MakeByReferenceType()));
                    methodb.Parameters.Add(new Mono.Cecil.ParameterDefinition("size", ParameterAttributes.None, mb.TypeSystem.Int32));
                    il = methodb.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldind_I);
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.MarshalFreeHGlobalMethod));
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Stind_I);
                    il.Emit(OpCodes.Ret);
                    mi = helper.AddMethod(func, methodb);
                    mi.stand = true;
                    return mi;
            }
            return null;
        }

        private void FixPointer()
        {
            if (fix_pointer_meth == null && comp_opt.RtlPABCSystemType != null)
            {
                fix_pointer_meth = mb.ImportReference(comp_opt.RtlPABCSystemType)
                    .Resolve()
                    .GetMethods()
                    .First(item=> item.Name == "__FixPointer");
            }
            if (fix_pointer_meth != null)
            {
                il.Emit(OpCodes.Call, fix_pointer_meth);
                il.Emit(OpCodes.Pop);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, (int)GCHandleType.Pinned);
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.GCHandleAllocPinned));
                il.Emit(OpCodes.Pop);
            }
        }

        //вызов глобальной процедуры
        public override void visit(SemanticTree.ICommonNamespaceFunctionCallNode value)
        {
            
            MethInfo meth = helper.GetMethod(value.namespace_function);
            IExpressionNode[] real_parameters = value.real_parameters;
            if (comp_opt.dbg_attrs == DebugAttributes.Release && meth != null && has_debug_conditional_attr(value.namespace_function.Attributes))
                return;
            //если это стандартная (New или Dispose)
            if (meth == null || meth.stand)
            {
                if (GenerateStandardFuncCall(value, il))
                    return;
                if (meth == null)
                    meth = MakeStandardFunc(value);
                Mono.Cecil.TypeReference ptrt = null;
                TypeInfo ti = null;
                if (real_parameters[0].type is IRefTypeNode)
                {
                    IRefTypeNode rtn = (IRefTypeNode)real_parameters[0].type;
                    ti = helper.GetTypeReference(rtn.pointed_type);
                    ptrt = ti.tp;
                }
                else
                {
                    ti = helper.GetTypeReference(real_parameters[0].type);
                    ptrt = ti.tp.GetElementType();
                }
                is_addr = true;
                real_parameters[0].visit(this);
                is_addr = false;
                PushSize(ptrt);
                il.Emit(OpCodes.Call, meth.mi);
                if (value.namespace_function.SpecialFunctionKind == SpecialFunctionKind.New && real_parameters[0].type is IRefTypeNode)
                {
                    ITypeNode tn = (real_parameters[0].type as IRefTypeNode).pointed_type;
                    if (tn.type_special_kind == type_special_kind.array_wrapper)
                    {
                        ICommonTypeNode ctn = tn as ICommonTypeNode;
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Newobj, helper.GetTypeReference(ctn).def_cnstr);
                        il.Emit(OpCodes.Stind_Ref);
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Ldind_Ref);
                        FixPointer();
                    }
                    else if (tn.type_special_kind == type_special_kind.set_type)
                    {
                        ICommonNamespaceFunctionNode cnfn = PascalABCCompiler.SystemLibrary.SystemLibInitializer.TypedSetInitProcedureWithBounds.sym_info as ICommonNamespaceFunctionNode;
                        real_parameters[0].visit(this);
                        IConstantNode cn1 = (tn as ICommonTypeNode).lower_value;
                        IConstantNode cn2 = (tn as ICommonTypeNode).upper_value;
                        if (cn1 != null && cn2 != null)
                        {
                            cn1.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn1.type).tp);
                            cn2.visit(this);
                            il.Emit(OpCodes.Box, helper.GetTypeReference(cn2.type).tp);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldnull);
                        }
                        il.Emit(OpCodes.Newobj, ti.def_cnstr);
                        il.Emit(OpCodes.Stind_Ref);
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Ldind_Ref);
                        FixPointer();
                    }
                    else if (tn.type_special_kind == type_special_kind.typed_file)
                    {
                        NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference((tn as ICommonTypeNode).element_type).tp);
                        il.Emit(OpCodes.Newobj, ti.def_cnstr);
                        il.Emit(OpCodes.Stind_Ref);
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Ldind_Ref);
                        FixPointer();
                    }
                    else if (tn.type_special_kind == type_special_kind.short_string)
                    {
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Ldstr, "");
                        il.Emit(OpCodes.Stind_Ref);
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Ldind_Ref);
                        FixPointer();
                    }
                    else if (tn.is_value_type && tn is ICommonTypeNode)
                    {
                        TypeInfo ti2 = helper.GetTypeReference(tn);
                        if (ti2.init_meth != null)
                        {
                            real_parameters[0].visit(this);
                            il.Emit(OpCodes.Call, ti2.init_meth);
                        }
                        if (ti2.fix_meth != null)
                        {
                            real_parameters[0].visit(this);
                            il.Emit(OpCodes.Call, ti2.fix_meth);
                        }
                    }
                }
                return;
            }
            bool tmp_dot = is_dot_expr;
            is_dot_expr = false;
            
            Mono.Cecil.MethodReference mi = meth.mi;
            IParameterNode[] parameters = value.namespace_function.parameters;
            EmitArguments(parameters, real_parameters);
            il.Emit(OpCodes.Call, mi);
            EmitFreePinnedVariables();
            if (tmp_dot)
            {
                if (value.namespace_function.return_value_type != null && value.namespace_function.return_value_type.is_value_type && !NETGeneratorTools.IsPointer(mi.ReturnType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = (mi.ReturnType.HasGenericParameters || mi.ReturnType.IsGenericParameter) ?
                        new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.namespace_function.return_value_type).tp) :
                        new Mono.Cecil.Cil.VariableDefinition(mi.ReturnType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
                is_dot_expr = tmp_dot;
            }
            else
                if (meth.is_ptr_ret_type && is_addr == false)
                    il.Emit(OpCodes.Ldobj, helper.GetTypeReference(value.namespace_function.return_value_type).tp);
            if (mi.ReturnType.FullName == mb.TypeSystem.Void.FullName)
                il.Emit(OpCodes.Nop);
            //if (is_stmt == true) il.Emit(OpCodes.Pop);
        }
        
        private void EmitArguments(IParameterNode[] parameters, IExpressionNode[] real_parameters)
        {
        	bool is_comp_gen = false;
        	for (int i = 0; i < real_parameters.Length; i++)
            {
                if (real_parameters[i] is INullConstantNode && parameters[i].type.is_nullable_type)
                {
        			Mono.Cecil.TypeReference tp = helper.GetTypeReference(parameters[i].type).tp;
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
                    il.Body.Variables.Add(lb);
        			il.Emit(OpCodes.Ldloca, lb);
        			il.Emit(OpCodes.Initobj, tp);
        			il.Emit(OpCodes.Ldloc, lb);
        			continue;
        		}
                if (parameters[i].parameter_type == parameter_type.var)
                    is_addr = true;
                ITypeNode ctn = real_parameters[i].type;
                TypeInfo ti = null;
                if (parameters[i].type is ICompiledTypeNode && (parameters[i].type as ICompiledTypeNode).compiled_type.FullName == mb.TypeSystem.Char.FullName && parameters[i].parameter_type == parameter_type.var
                    && real_parameters[i] is ISimpleArrayIndexingNode && helper.GetTypeReference((real_parameters[i] as ISimpleArrayIndexingNode).array.type).tp.FullName == mb.TypeSystem.String.FullName)
                {
                    copy_string = true;
                }
                //(ssyy) moved up
                ITypeNode tn2 = parameters[i].type;
                ICompiledTypeNode ctn2 = tn2 as ICompiledTypeNode;
                ITypeNode ctn3 = real_parameters[i].type;
                ITypeNode ctn4 = real_parameters[i].conversion_type;
                bool use_stn4 = false;
                //(ssyy) 07.12.2007 При боксировке нужно вызывать Ldsfld вместо Ldsflda.
                //Дополнительная проверка введена именно для этого.
                bool box_awaited =
                    (ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.compiled_type.FullName == TypeFactory.EnumType.FullName) || tn2.IsInterface) && !(real_parameters[i] is SemanticTree.INullConstantNode) 
                	&& (ctn3.is_value_type || ctn3.is_generic_parameter);
                if (!box_awaited && (ctn2 != null && ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || tn2.IsInterface) && !(real_parameters[i] is SemanticTree.INullConstantNode) 
                	&& ctn4 != null && (ctn4.is_value_type || ctn4.is_generic_parameter))
                {
                	box_awaited = true;
                	use_stn4 = true;
                }
                //if (ctn4 != null && ctn4.is_value_type && (ctn4 is ICommonTypeNode || ctn4 is ICompiledTypeNode && !TypeFactory.IsStandType((ctn4 as ICompiledTypeNode).compiled_type)) && ctn3 is ICompiledTypeNode && (ctn3 as ICompiledTypeNode).compiled_type != TypeFactory.ObjectType)
                //    box_awaited = false;
                if (!(real_parameters[i] is INullConstantNode))
                {
                    ti = helper.GetTypeReference(ctn);
                    if (ti.clone_meth != null && ti.tp != null && ti.tp.IsValueType && !box_awaited && !parameters[i].is_const)
                        is_dot_expr = true;
                }
                is_comp_gen = CheckForCompilerGenerated(real_parameters[i]);
                real_parameters[i].visit(this);
                is_dot_expr = false;
                CallCloneIfNeed(il, parameters[i], real_parameters[i]);
                if (box_awaited)
                {
                	if (use_stn4)
                		il.Emit(OpCodes.Box, helper.GetTypeReference(ctn4).tp);
                	else
                    	il.Emit(OpCodes.Box, helper.GetTypeReference(ctn3).tp);
                }
                is_addr = false;
            }
        }
        
        private void EmitFreePinnedVariables()
        {
            /*foreach (LocalBuilder lb in pinned_variables)
            {
                il.Emit(OpCodes.Ldloca, lb);
                il.Emit(OpCodes.Call, TypeFactory.GCHandleFreeMethod);
            }
            pinned_variables.Clear();*/
        }

        //присваивание глобальной переменной
        private void AssignToNamespaceVariableNode(IExpressionNode to, IExpressionNode from)
        {
            INamespaceVariableReferenceNode var = (INamespaceVariableReferenceNode)to;
            //получаем переменную
            VarInfo vi = helper.GetVariable(var.variable);
            Mono.Cecil.FieldDefinition fb = vi.fb;
            TypeInfo ti = helper.GetTypeReference(to.type);
            if (to.type.is_value_type)
            {
                //ti = helper.GetTypeReference(to.type);
                if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type)
                    il.Emit(OpCodes.Ldsflda, fb);
            }
            else if (to.type.type_special_kind == type_special_kind.set_type && !in_var_init)
            {
                il.Emit(OpCodes.Ldsfld, fb);
                from.visit(this);
                il.Emit(OpCodes.Call, ti.assign_meth);
                return;
            }
            else ti = null;
            if (from is INullConstantNode && to.type.is_nullable_type)
            {
            	il.Emit(OpCodes.Initobj, ti.tp);
            	return;
            }
            //что присвоить
            from.visit(this);
            if (ti != null && ti.assign_meth != null)
            {
                il.Emit(OpCodes.Call, ti.assign_meth);
                return;
            }
            
            //это если например переменной типа object присваивается число
            EmitBox(from, fb.FieldType);

            CheckArrayAssign(to, from, il);

            //присваиваем
            il.Emit(OpCodes.Stsfld, fb);
        }

        /// <summary>
        /// Необходимо, тк в особых случаях прямой вызов .IsEnum приводит к исключению
        /// </summary>
        private bool TypeIsEnum(Mono.Cecil.TypeReference T)
        {
            return !(T is Mono.Cecil.TypeSpecification) && T.Resolve().IsEnum;
        }


        private bool TypeIsInterface(Mono.Cecil.TypeReference T)
        {
            return T.Resolve()?.IsInterface ?? false;
        }

        private bool TypeIsClass(Mono.Cecil.TypeReference T)
        {
            return T.Resolve().IsClass;
        }

        private bool EmitBox(IExpressionNode from, Mono.Cecil.TypeReference LocalType)
        {
            if ((from.type.is_value_type || from.type.is_generic_parameter) && !(from is SemanticTree.INullConstantNode) && (LocalType.FullName == mb.TypeSystem.Object.FullName || TypeIsInterface(LocalType) || LocalType.FullName == TypeFactory.EnumType.FullName))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.type).tp);//упаковка
                return true;
            }
            if (from.conversion_type != null && from.conversion_type.is_value_type && !(from is SemanticTree.INullConstantNode) && (LocalType.FullName == mb.TypeSystem.Object.FullName || TypeIsInterface(LocalType)))
            {
            	il.Emit(OpCodes.Box, helper.GetTypeReference(from.conversion_type).tp);
            }
            return false;
        }

        internal void CheckArrayAssign(IExpressionNode to, IExpressionNode from, Mono.Cecil.Cil.ILProcessor il)
        {
            //DarkStar Add 07.11.06 02:32
            //Массив присваиваем массиву=>надо вызвать копирование
            TypeInfo ti_l = helper.GetTypeReference(to.type);
            TypeInfo ti_r = helper.GetTypeReference(from.type);
            if (ti_l.is_arr && ti_r.is_arr && !(to is ILocalBlockVariableReferenceNode && (to as ILocalBlockVariableReferenceNode).Variable.name.StartsWith("$TV")))
            {
                il.Emit(OpCodes.Call, ti_r.clone_meth);
            }
            else if (ti_l.is_set && ti_r.is_set)
            {
                //if (!(from is ICommonConstructorCall))
                //il.Emit(OpCodes.Callvirt, ti_r.clone_meth);
            }
        }

        //присваивание локальной переменной
        private void AssignToLocalVariableNode(IExpressionNode to, IExpressionNode from)
        {
            IReferenceNode var = (IReferenceNode)to;
            VarInfo vi = helper.GetVariable(var.Variable);
            if (vi.kind == VarKind.vkLocal)
            {
                Mono.Cecil.Cil.VariableDefinition lb = vi.lb;
                TypeInfo ti = helper.GetTypeReference(to.type);
                if (to.type.is_value_type)
                {
                    //ti = helper.GetTypeReference(to.type);
                    if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type)
                        il.Emit(OpCodes.Ldloca, lb);
                    
                }
                else if (to.type.type_special_kind == type_special_kind.set_type && !in_var_init)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    from.visit(this);
                    il.Emit(OpCodes.Call, ti.assign_meth);
                    return;
                }
                else ti = null;
                if (from is INullConstantNode && to.type.is_nullable_type)
                {
                	il.Emit(OpCodes.Initobj, ti.tp);
                	return;
                }
                //что присвоить
                from.visit(this);
                if (ti != null && ti.assign_meth != null)
                {
                    il.Emit(OpCodes.Call, ti.assign_meth);
                    return;
                }
                EmitBox(from, lb.VariableType);
                CheckArrayAssign(to, from, il);
                il.Emit(OpCodes.Stloc, lb);
            }
            else if (vi.kind == VarKind.vkNonLocal)
            {
                Mono.Cecil.FieldDefinition fb = vi.fb;
                MethInfo cur_mi = smi.Peek();
                int dist = smi.Peek().num_scope - vi.meth.num_scope;
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                    cur_mi = cur_mi.up_meth;
                }
                TypeInfo ti = helper.GetTypeReference(to.type);
                if (to.type.is_value_type)
                {
                    //ti = helper.GetTypeReference(to.type);
                    if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type) 
                    	il.Emit(OpCodes.Ldflda, fb);
                }
                else if (to.type.type_special_kind == type_special_kind.set_type && !in_var_init)
                {
                    il.Emit(OpCodes.Ldfld, fb);
                    from.visit(this);
                    il.Emit(OpCodes.Call, ti.assign_meth);
                    return;
                }
                else ti = null;
                if (from is INullConstantNode && to.type.is_nullable_type)
                {
                	il.Emit(OpCodes.Initobj, ti.tp);
                	return;
                }
                //что присвоить
                from.visit(this);
                if (ti != null && ti.assign_meth != null)
                {
                    il.Emit(OpCodes.Call, ti.assign_meth);
                    return;
                }
                
                EmitBox(from, fb.FieldType);
                CheckArrayAssign(to, from, il);
                il.Emit(OpCodes.Stfld, fb);
            }
        }

        private void BoxAssignToParameter(IExpressionNode to, IExpressionNode from)
        {
            ICompiledTypeNode ctn2 = to.type as ICompiledTypeNode;
            if ((from.type.is_value_type || from.type.is_generic_parameter) && ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.type).tp);
            }
            else if (from.conversion_type != null && (from.type.is_value_type || from.type.is_generic_parameter) && ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
            {
            	il.Emit(OpCodes.Box, helper.GetTypeReference(from.conversion_type).tp);
            }
            CheckArrayAssign(to, from, il);
        }

        private void StoreParameterByReference(Mono.Cecil.TypeReference t)
        {
            NETGeneratorTools.PushStind(il, t);
        }

        //присвоение параметру
        //полная бяка, например нужно присвоить нелокальному var-параметру какое-то значение
        private void AssignToParameterNode(IExpressionNode to, IExpressionNode from)
        {
            ICommonParameterReferenceNode var = (ICommonParameterReferenceNode)to;
            ParamInfo pi = helper.GetParameter(var.parameter);
            if (pi.kind == ParamKind.pkNone)//если параметр локальный
            {
                Mono.Cecil.ParameterDefinition pb = pi.pb;
                //byte pos = (byte)(pb.Position-1);
                //***********************Kolay modified**********************
                ushort pos = (ushort)(pb.Index);
                if (is_constructor || cur_meth.IsStatic == false)
                    pos = (ushort)pb.Index;
                else
                    pos = (ushort)(pb.Index);
                //***********************End of Kolay modified**********************
                if (var.parameter.parameter_type == parameter_type.value)
                {
                    TypeInfo ti = helper.GetTypeReference(to.type);
                    if (to.type.is_value_type)
                    {
                        if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type) 
                        	il.Emit(OpCodes.Ldarga, pos);
                    }
                    else if (to.type.type_special_kind == type_special_kind.set_type)
                    {
                        il.Emit(OpCodes.Ldarg, pos);
                        from.visit(this);
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    else ti = null;
                    if (from is INullConstantNode && to.type.is_nullable_type)
                    {
                    	il.Emit(OpCodes.Initobj, ti.tp);
                    	return;
                    }
                    //что присвоить
                    from.visit(this);
                    if (ti != null && ti.assign_meth != null)
                    {
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    
                    BoxAssignToParameter(to, from);
                    //il.Emit(OpCodes.Dup);
                    if (pos <= 255) 
                    	il.Emit(OpCodes.Starg_S, (byte)pos);
                    else 
                    	il.Emit(OpCodes.Starg, pos);
                }
                else
                {
                    TypeInfo ti = helper.GetTypeReference(to.type);
                    if (to.type.is_value_type)
                    {
                        //ti = helper.GetTypeReference(to.type);
                        if (ti.assign_meth != null)
                        {
                            //здесь надо быть внимательнее
                            il.Emit(OpCodes.Ldarg, pos);
                            from.visit(this);
                            il.Emit(OpCodes.Call, ti.assign_meth);
                            return;
                        }
                        if (from is INullConstantNode && to.type.is_nullable_type)
                    	{
                        	il.Emit(OpCodes.Ldarg, pos);
                    		il.Emit(OpCodes.Initobj, ti.tp);
                    		return;
                    	}
                    }
                    else if (to.type.type_special_kind == type_special_kind.set_type)
                    {
                        il.Emit(OpCodes.Ldarg, pos);
                        il.Emit(OpCodes.Ldind_Ref);
                        from.visit(this);
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    else ti = null;
                    PushParameter(pos);
                    from.visit(this);
                    BoxAssignToParameter(to, from);
                    //il.Emit(OpCodes.Dup);
                    ti = helper.GetTypeReference(var.type);
                    StoreParameterByReference(ti.tp);
                }
            }
            else//иначе нелокальный
            {
                Mono.Cecil.FieldDefinition fb = pi.fb;
                MethInfo cur_mi = (MethInfo)smi.Peek();
                int dist = ((MethInfo)smi.Peek()).num_scope - pi.meth.num_scope;
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                    cur_mi = cur_mi.up_meth;
                }

                if (var.parameter.parameter_type == parameter_type.value)
                {
                    TypeInfo ti = helper.GetTypeReference(to.type);
                    if (to.type.is_value_type)
                    {
                        //ti = helper.GetTypeReference(to.type);
                        if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type) 
                        	il.Emit(OpCodes.Ldflda, fb);
                    }
                    else if (to.type.type_special_kind == type_special_kind.set_type)
                    {
                        il.Emit(OpCodes.Ldfld, fb);
                        from.visit(this);
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    else ti = null;
                    if (from is INullConstantNode && to.type.is_nullable_type)
                    {
                    	il.Emit(OpCodes.Initobj, ti.tp);
                    	return;
                    }
                    //что присвоить
                    from.visit(this);
                    if (ti != null && ti.assign_meth != null)
                    {
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    
                    BoxAssignToParameter(to, from);
                    //il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Stfld, fb);
                }
                else
                {
                    TypeInfo ti = helper.GetTypeReference(to.type);
                    il.Emit(OpCodes.Ldfld, fb);

                    if (to.type.is_value_type)
                    {
                        //ti = helper.GetTypeReference(to.type);
                        if (ti.assign_meth != null)
                        {
                            il.Emit(OpCodes.Call, ti.assign_meth);
                            return;
                        }
                        if (from is INullConstantNode && to.type.is_nullable_type)
                    	{
                    		il.Emit(OpCodes.Initobj, ti.tp);
                    		return;
                    	}
                    }
                    else if (to.type.type_special_kind == type_special_kind.set_type)
                    {
                        //il.Emit(OpCodes.Ldarg, pos);
                        //il.Emit(OpCodes.Ldind_Ref);
                        //from.visit(this);
                        il.Emit(OpCodes.Ldind_Ref);
                        from.visit(this);
                        il.Emit(OpCodes.Call, ti.assign_meth);
                        return;
                    }
                    else ti = null;
                    from.visit(this);
                    BoxAssignToParameter(to, from);
                    //il.Emit(OpCodes.Dup);
                    ti = helper.GetTypeReference(var.type);
                    StoreParameterByReference(ti.tp);
                }
            }
        }

        //присвоение полю
        private void AssignToField(IExpressionNode to, IExpressionNode from)
        {
            ICommonClassFieldReferenceNode value = (ICommonClassFieldReferenceNode)to;
#if DEBUG
            /*if (value.field.name == "XYZW")
            {
                var y = value.field.GetHashCode();
            } */
#endif
            FldInfo fi_info = helper.GetField(value.field);
            Mono.Cecil.FieldReference fi = fi_info.fi;
            is_dot_expr = true;
            has_dereferences = false;
            is_field_reference = true;
            value.obj.visit(this);
            is_field_reference = false;
            bool has_dereferences_tmp = has_dereferences;
            has_dereferences = false;
            is_dot_expr = false;
            TypeInfo ti = helper.GetTypeReference(to.type);
            if (to.type.is_value_type)
            {
                if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type)
                    il.Emit(OpCodes.Ldflda, fi);
            }
            else if (to.type.type_special_kind == type_special_kind.set_type && !in_var_init)
            {
                il.Emit(OpCodes.Ldfld, fi);
                from.visit(this);
                il.Emit(OpCodes.Call, ti.assign_meth);
                return;
            }
            else ti = null;
            if (from is INullConstantNode && to.type.is_nullable_type)
            {
            	il.Emit(OpCodes.Initobj, ti.tp);
                return;
           	}
            //что присвоить
            from.visit(this);
            if (ti != null && ti.assign_meth != null)
            {
                il.Emit(OpCodes.Call, ti.assign_meth);
                if (has_dereferences_tmp)
                {
                    if (ti.fix_meth != null)
                    {
                        is_dot_expr = true;
                        value.obj.visit(this);
                        is_dot_expr = false;
                        il.Emit(OpCodes.Ldflda, fi);
                        il.Emit(OpCodes.Call, ti.fix_meth);
                    }
                }
                return;
            }
            
            EmitBox(from, fi_info.field_type);
            CheckArrayAssign(to, from, il);
            il.Emit(OpCodes.Stfld, fi);
            if (has_dereferences_tmp && TypeNeedToFix(to.type))
            {
                is_dot_expr = true;
                value.obj.visit(this);
                is_dot_expr = false;
                il.Emit(OpCodes.Ldfld, fi);
                FixPointer();
            }
        }

        //присвоение статическому полю
        private void AssignToStaticField(IExpressionNode to, IExpressionNode from)
        {
            IStaticCommonClassFieldReferenceNode value = (IStaticCommonClassFieldReferenceNode)to;
            FldInfo fi_info = helper.GetField(value.static_field);
            Mono.Cecil.FieldReference fi = fi_info.fi;
            TypeInfo ti = helper.GetTypeReference(to.type);
            if (to.type.is_value_type)
            {
                //ti = helper.GetTypeReference(to.type);
                if (ti.assign_meth != null || from is INullConstantNode && to.type.is_nullable_type) 
                	il.Emit(OpCodes.Ldsflda, fi);
            }
            else if (to.type.type_special_kind == type_special_kind.set_type)
            {
                il.Emit(OpCodes.Ldsfld, fi);
                from.visit(this);
                il.Emit(OpCodes.Call, ti.assign_meth);
                return;
            }
            else ti = null;
            if (from is INullConstantNode && to.type.is_nullable_type)
            {
            	il.Emit(OpCodes.Initobj, ti.tp);
                return;
           	}
            //что присвоить
            from.visit(this);
            if (ti != null && ti.assign_meth != null)
            {
                il.Emit(OpCodes.Call, ti.assign_meth);
                return;
            }
            if (ti != null)
                EmitBox(from, ti.tp);
            else
                EmitBox(from, fi_info.field_type);
            CheckArrayAssign(to, from, il);
            il.Emit(OpCodes.Stsfld, fi);
        }

        private void AssignToCompiledField(IExpressionNode to, IExpressionNode from)
        {
            ICompiledFieldReferenceNode value = (ICompiledFieldReferenceNode)to;
            Mono.Cecil.FieldReference fi = mb.ImportReference(value.field.compiled_field);
            is_dot_expr = true;
            value.obj.visit(this);
            is_dot_expr = false;
            from.visit(this);
            EmitBox(from, fi.FieldType);
            il.Emit(OpCodes.Stfld, fi);
        }

        private void AssignToStaticCompiledField(IExpressionNode to, IExpressionNode from)
        {
            IStaticCompiledFieldReferenceNode value = (IStaticCompiledFieldReferenceNode)to;
            Mono.Cecil.FieldReference fi = mb.ImportReference(value.static_field.compiled_field);
            from.visit(this);
            EmitBox(from, fi.FieldType);
            il.Emit(OpCodes.Stsfld, fi);
        }

        //присвоение элементу массива
        //
        private void AssignToSimpleArrayNode(IExpressionNode to, IExpressionNode from)
        {
            ISimpleArrayIndexingNode value = (ISimpleArrayIndexingNode)to;
            TypeInfo ti = helper.GetTypeReference(value.array.type);
            ISimpleArrayNode arr_type = value.array.type as ISimpleArrayNode;
            TypeInfo elem_ti = null;
            if (arr_type != null)
                elem_ti = helper.GetTypeReference(arr_type.element_type);
            else if (value.array.type.type_special_kind == type_special_kind.array_kind && value.array.type is ICommonTypeNode)
                elem_ti = helper.GetTypeReference(value.array.type.element_type);
            Mono.Cecil.TypeReference elem_type = null;
            if (elem_ti != null)
                elem_type = elem_ti.tp;
            else
                elem_type = ((Mono.Cecil.ArrayType)ti.tp).ElementType;
            value.array.visit(this);
            Mono.Cecil.MethodReference get_meth = null;
			Mono.Cecil.MethodReference addr_meth = null;
			Mono.Cecil.MethodReference set_meth = null;
            Mono.Cecil.Cil.VariableDefinition index_lb = null;
            if (value.indices == null)
            {
                value.index.visit(this);
                if (from is IBasicFunctionCallNode && (from as IBasicFunctionCallNode).real_parameters[0] == to && current_index_lb == null)
                {
                    index_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.index.type).tp);
                    il.Body.Variables.Add(index_lb);
					il.Emit(OpCodes.Stloc, index_lb);
                    il.Emit(OpCodes.Ldloc, index_lb);
                    current_index_lb = index_lb;

                }
            }
            else
            {
                List<Mono.Cecil.TypeReference> lst = new List<Mono.Cecil.TypeReference>();
                for (int i = 0; i < value.indices.Length; i++)
                    lst.Add(mb.TypeSystem.Int32);
                get_meth = new Mono.Cecil.MethodReference("Get", elem_type, ti.tp);
                get_meth.HasThis = true;
                foreach (var paramType in lst)
                    get_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
                addr_meth = new Mono.Cecil.MethodReference("Address", elem_type.MakeByReferenceType(), ti.tp);
                addr_meth.HasThis = true;
                foreach (var paramType in lst)
                    addr_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
                lst.Add(elem_type);
                set_meth = new Mono.Cecil.MethodReference("Set", mb.TypeSystem.Void, ti.tp);
                set_meth.HasThis = true;
                foreach (var paramType in lst)
                    set_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));

                for (int i = 0; i < value.indices.Length; i++)
                    value.indices[i].visit(this);
            }
            if (elem_type.IsValueType && !TypeFactory.IsStandType(elem_type) && !TypeIsEnum(elem_type) || to.type.is_nullable_type)
            {
                if (value.indices == null)
                    il.Emit(OpCodes.Ldelema, elem_type);
            }
            else if (elem_ti != null && elem_ti.assign_meth != null)
            {
                if (value.indices == null)
                    il.Emit(OpCodes.Ldelem_Ref);
                else
                    il.Emit(OpCodes.Call, get_meth);
            }
            if (from is INullConstantNode && to.type.is_nullable_type)
            {
                if (value.indices != null && addr_meth != null)
                    il.Emit(OpCodes.Call, addr_meth);
                il.Emit(OpCodes.Initobj, elem_type);
                
                return;
            }
            from.visit(this);
            if (elem_ti != null && elem_ti.assign_meth != null)
            {
                il.Emit(OpCodes.Call, elem_ti.assign_meth);
                return;
            }
            ICompiledTypeNode ctn2 = to.type as ICompiledTypeNode;
            if ((from.type.is_value_type || from.type.is_generic_parameter) && ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.type).tp);
            }
            else if ((from.type.is_value_type || from.type.is_generic_parameter) && to.type.IsInterface)
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.type).tp);
            }
            else if (from.conversion_type != null && (from.conversion_type.is_value_type || from.conversion_type.is_generic_parameter) && ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || ctn2.IsInterface))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.conversion_type).tp);
            }

            CheckArrayAssign(to, from, il);
            if (value.indices == null)
                NETGeneratorTools.PushStelem(il, elem_type);
            else
                il.Emit(OpCodes.Call, set_meth);
        }

        //присвоение например a^ := 1
        private void AssignToDereferenceNode(IExpressionNode to, IExpressionNode from)
        {
            IDereferenceNode value = (IDereferenceNode)to;
            TypeInfo ti = helper.GetTypeReference(to.type);
            value.derefered_expr.visit(this);
            if (ti != null && ti.assign_meth != null && !ti.tp.IsValueType)
                il.Emit(OpCodes.Ldind_Ref);
            from.visit(this);
            if (ti != null && ti.assign_meth != null)
            {
                il.Emit(OpCodes.Call, ti.assign_meth);
                if (ti.tp.IsValueType && ti.fix_meth != null)
                {
                    value.derefered_expr.visit(this);
                    il.Emit(OpCodes.Call, ti.fix_meth);
                }
                return;
            }
            //ICompiledTypeNode ctn = from.type as ICompiledTypeNode;
            ICompiledTypeNode ctn2 = to.type as ICompiledTypeNode;
            if ((from.type.is_value_type || from.type.is_generic_parameter) && 
                ctn2 != null && 
                (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || 
                (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || mb.ImportReference(ctn2.compiled_type).Resolve().IsInterface))
               )
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.type).tp);
            }
            else if (from.conversion_type != null && (from.conversion_type.is_value_type || from.conversion_type.is_generic_parameter) && ctn2 != null && (ctn2.compiled_type.FullName == mb.TypeSystem.Object.FullName || mb.ImportReference(ctn2.compiled_type).Resolve().IsInterface))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(from.conversion_type).tp);
            }
            CheckArrayAssign(to, from, il);
            NETGeneratorTools.PushStind(il, ti.tp);
            if (TypeNeedToFix(value.type))
            {
                value.derefered_expr.visit(this);
                il.Emit(OpCodes.Ldind_Ref);
                FixPointer();
            }
            else if (ti.tp.IsValueType && ti.fix_meth != null)
            {
                value.derefered_expr.visit(this);
                il.Emit(OpCodes.Call, ti.fix_meth);
            }
        }

        //присваивание
        private void ConvertAssignExpr(IExpressionNode to, IExpressionNode from)
        {
            if (to is INamespaceVariableReferenceNode)
            {
                AssignToNamespaceVariableNode(to, from);
            }
            else if (to is ILocalVariableReferenceNode || to is ILocalBlockVariableReferenceNode)
            {
                AssignToLocalVariableNode(to, from);
            }
            else if (to is ICommonParameterReferenceNode)
            {
                AssignToParameterNode(to, from);
            }
            else if (to is ICommonClassFieldReferenceNode)
            {
                AssignToField(to, from);
            }
            else if (to is IStaticCommonClassFieldReferenceNode)
            {
                AssignToStaticField(to, from);
            }
            else if (to is ICompiledFieldReferenceNode)
            {
                AssignToCompiledField(to, from);
            }
            else if (to is IStaticCompiledFieldReferenceNode)
            {
                AssignToStaticCompiledField(to, from);
            }
            else if (to is ISimpleArrayIndexingNode)
            {
                AssignToSimpleArrayNode(to, from);
            }
            else if (to is IDereferenceNode)
            {
                AssignToDereferenceNode(to, from);
            }
        }

        //перевод инкремента
        private void ConvertInc(IExpressionNode e)
        {
            Mono.Cecil.TypeReference tp = helper.GetTypeReference(e.type).tp;
            if (e is INamespaceVariableReferenceNode)
            {
                e.visit(this);

                //DS0030 fixed
                NETGeneratorTools.PushLdc(il, tp, 1);
                //il.Emit(OpCodes.Ldc_I4_1);

                il.Emit(OpCodes.Add);
                INamespaceVariableReferenceNode var = (INamespaceVariableReferenceNode)e;
                VarInfo vi = helper.GetVariable(var.variable);
                Mono.Cecil.FieldDefinition fb = vi.fb;
                il.Emit(OpCodes.Stsfld, fb);
            }
            else if (e is ILocalVariableReferenceNode || e is ILocalBlockVariableReferenceNode)
            {
                IReferenceNode var = (IReferenceNode)e;
                VarInfo vi = helper.GetVariable(var.Variable);
                if (vi.kind == VarKind.vkLocal)
                {
                    Mono.Cecil.Cil.VariableDefinition lb = vi.lb;
                    e.visit(this);
                    if (vi.lb.VariableType.FullName != mb.TypeSystem.Boolean.FullName)
                    {
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);

                        il.Emit(OpCodes.Add);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                    }
                    il.Emit(OpCodes.Stloc, lb);
                }
                else if (vi.kind == VarKind.vkNonLocal)
                {
                    Mono.Cecil.FieldDefinition fb = vi.fb;
                    MethInfo cur_mi = (MethInfo)smi.Peek();
                    int dist = ((MethInfo)smi.Peek()).num_scope - vi.meth.num_scope;
                    il.Emit(OpCodes.Ldloc, cur_mi.frame);
                    for (int i = 0; i < dist; i++)
                    {
                        il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                        cur_mi = cur_mi.up_meth;
                    }
                    e.visit(this);
                    if (vi.fb.FieldType.FullName != mb.TypeSystem.Boolean.FullName)
                    {
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);

                        il.Emit(OpCodes.Add);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                    }
                    il.Emit(OpCodes.Stfld, fb);
                }
            }
            else if (e is ICommonParameterReferenceNode)
            {
                ICommonParameterReferenceNode var = (ICommonParameterReferenceNode)e;
                ParamInfo pi = helper.GetParameter(var.parameter);
                if (pi.kind == ParamKind.pkNone)
                {
                    Mono.Cecil.ParameterDefinition pb = pi.pb;
                    //byte pos = (byte)(pb.Position-1);
                    //***********************Kolay modified**********************
                    byte pos = (byte)(pb.Index);
                    if (is_constructor || cur_meth.IsStatic == false) pos = (byte)pb.Index;
                    else pos = (byte)(pb.Index);
                    //***********************End of Kolay modified**********************
                    if (var.parameter.parameter_type == parameter_type.value)
                    {
                        e.visit(this);
                        if (helper.GetTypeReference(var.parameter.type).tp.FullName != mb.TypeSystem.Boolean.FullName)
                        {
                            //DS0030 fixed
                            NETGeneratorTools.PushLdc(il, tp, 1);
                            //il.Emit(OpCodes.Ldc_I4_1);

                            il.Emit(OpCodes.Add);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }

                        if (pos <= 255) il.Emit(OpCodes.Starg_S, pos);
                        else il.Emit(OpCodes.Starg, pos);
                    }
                    else
                    {
                        PushParameter(pos);
                        e.visit(this);
                        if (helper.GetTypeReference(var.parameter.type).tp.FullName != mb.TypeSystem.Boolean.FullName)
                        {
                            //DS0030 fixed
                            NETGeneratorTools.PushLdc(il, tp, 1);
                            //il.Emit(OpCodes.Ldc_I4_1);

                            il.Emit(OpCodes.Add);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        TypeInfo ti = helper.GetTypeReference(var.type);
                        StoreParameterByReference(ti.tp);
                    }
                }
                else
                {
                    Mono.Cecil.FieldDefinition fb = pi.fb;
                    MethInfo cur_mi = (MethInfo)smi.Peek();
                    int dist = ((MethInfo)smi.Peek()).num_scope - pi.meth.num_scope;
                    il.Emit(OpCodes.Ldloc, cur_mi.frame);
                    for (int i = 0; i < dist; i++)
                    {
                        il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                        cur_mi = cur_mi.up_meth;
                    }

                    if (var.parameter.parameter_type == parameter_type.value)
                    {
                        e.visit(this);
                        if (fb.FieldType.FullName != mb.TypeSystem.Boolean.FullName)
                        {
                            //DS0030 fixed
                            NETGeneratorTools.PushLdc(il, tp, 1);
                            //il.Emit(OpCodes.Ldc_I4_1);

                            il.Emit(OpCodes.Add);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        il.Emit(OpCodes.Stfld, fb);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldfld, fb);
                        e.visit(this);
                        if (fb.FieldType.FullName != mb.TypeSystem.Boolean.FullName)
                        {
                            //DS0030 fixed
                            NETGeneratorTools.PushLdc(il, tp, 1);
                            //il.Emit(OpCodes.Ldc_I4_1);

                            il.Emit(OpCodes.Add);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        TypeInfo ti = helper.GetTypeReference(var.type);
                        StoreParameterByReference(ti.tp);
                    }
                }
            }
        }

        //перевод декремента
        private void ConvertDec(IExpressionNode e)
        {
            Mono.Cecil.TypeReference tp = helper.GetTypeReference(e.type).tp;
            if (e is INamespaceVariableReferenceNode)
            {
                e.visit(this);
                //DS0030 fixed
                NETGeneratorTools.PushLdc(il, tp, 1);
                //il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Sub);
                INamespaceVariableReferenceNode var = (INamespaceVariableReferenceNode)e;
                VarInfo vi = helper.GetVariable(var.variable);
                Mono.Cecil.FieldDefinition fb = vi.fb;
                il.Emit(OpCodes.Stsfld, fb);
            }
            else if (e is ILocalVariableReferenceNode || e is ILocalBlockVariableReferenceNode)
            {
                IReferenceNode var = (IReferenceNode)e;
                VarInfo vi = helper.GetVariable(var.Variable);
                if (vi.kind == VarKind.vkLocal)
                {
                    Mono.Cecil.Cil.VariableDefinition lb = vi.lb;
                    e.visit(this);
                    //DS0030 fixed
                    NETGeneratorTools.PushLdc(il, tp, 1);
                    //il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Stloc, lb);
                }
                else if (vi.kind == VarKind.vkNonLocal)
                {
                    Mono.Cecil.FieldDefinition fb = vi.fb;
                    MethInfo cur_mi = smi.Peek();
                    int dist = (smi.Peek()).num_scope - vi.meth.num_scope;
                    il.Emit(OpCodes.Ldloc, cur_mi.frame);
                    for (int i = 0; i < dist; i++)
                    {
                        il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                        cur_mi = cur_mi.up_meth;
                    }
                    e.visit(this);
                    //DS0030 fixed
                    NETGeneratorTools.PushLdc(il, tp, 1);
                    //il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Stfld, fb);
                }
            }
            else if (e is ICommonParameterReferenceNode)
            {
                ICommonParameterReferenceNode var = (ICommonParameterReferenceNode)e;
                ParamInfo pi = helper.GetParameter(var.parameter);
                if (pi.kind == ParamKind.pkNone)
                {
                    Mono.Cecil.ParameterDefinition pb = pi.pb;
                    //byte pos = (byte)(pb.Position-1);
                    //***********************Kolay modified**********************
                    byte pos = (byte)(pb.Index);
                    if (is_constructor || cur_meth.IsStatic == false) pos = (byte)pb.Index;
                    else pos = (byte)(pb.Index);
                    //***********************End of Kolay modified**********************
                    if (var.parameter.parameter_type == parameter_type.value)
                    {
                        e.visit(this);
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Sub);

                        if (pos <= 255) il.Emit(OpCodes.Starg_S, pos);
                        else il.Emit(OpCodes.Starg, pos);
                    }
                    else
                    {
                        PushParameter(pos);
                        e.visit(this);
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Sub);
                        TypeInfo ti = helper.GetTypeReference(var.type);
                        StoreParameterByReference(ti.tp);
                    }
                }
                else
                {
                    Mono.Cecil.FieldDefinition fb = pi.fb;
                    MethInfo cur_mi = smi.Peek();
                    int dist = (smi.Peek()).num_scope - pi.meth.num_scope;
                    il.Emit(OpCodes.Ldloc, cur_mi.frame);
                    for (int i = 0; i < dist; i++)
                    {
                        il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                        cur_mi = cur_mi.up_meth;
                    }

                    if (var.parameter.parameter_type == parameter_type.value)
                    {
                        e.visit(this);
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Sub);
                        il.Emit(OpCodes.Stfld, fb);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldfld, fb);
                        e.visit(this);
                        //DS0030 fixed
                        NETGeneratorTools.PushLdc(il, tp, 1);
                        //il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Sub);
                        TypeInfo ti = helper.GetTypeReference(var.type);
                        StoreParameterByReference(ti.tp);
                    }
                }
            }
        }

        //перевод бинарных, унарных и проч. выражений
        public override void visit(SemanticTree.IBasicFunctionCallNode value)
        {
            make_next_spoint = true;
            bool tmp_dot = is_dot_expr;
            IExpressionNode[] real_parameters = value.real_parameters;
            is_dot_expr = false;

            {
                //(ssyy) 29.01.2008 Внёс band, bor под switch
                basic_function_type ft = value.basic_function.basic_function_type;
                if ((ft == basic_function_type.objeq || ft == basic_function_type.objnoteq) && real_parameters[0].type.is_value_type && 
                    (real_parameters[0].type is ICompiledTypeNode && !TypeFactory.IsStandType(mb.ImportReference((real_parameters[0].type as ICompiledTypeNode).compiled_type)) || real_parameters[0].type is ICompiledGenericTypeInstance) && !real_parameters[0].type.is_nullable_type
                     && real_parameters[1].type.is_value_type &&
                    (real_parameters[1].type is ICompiledTypeNode && !TypeFactory.IsStandType(mb.ImportReference((real_parameters[1].type as ICompiledTypeNode).compiled_type)) || real_parameters[1].type is ICompiledGenericTypeInstance) && !real_parameters[1].type.is_nullable_type)
                {
                    ICompiledTypeNode ctn1 = real_parameters[0].type as ICompiledTypeNode;
                    ICompiledTypeNode ctn2 = real_parameters[1].type as ICompiledTypeNode;
                    if (ctn1 == null)
                        ctn1 = (real_parameters[0].type as ICompiledGenericTypeInstance).original_generic as ICompiledTypeNode;
                    if (ctn2 == null)
                        ctn2 = (real_parameters[1].type as ICompiledGenericTypeInstance).original_generic as ICompiledTypeNode;
                    Mono.Cecil.TypeReference t1 = mb.ImportReference(ctn1.compiled_type);
					Mono.Cecil.TypeReference t2 = mb.ImportReference(ctn2.compiled_type);
                    if (real_parameters[0].type is ICompiledGenericTypeInstance)
                        t1 = helper.GetTypeReference(real_parameters[0].type).tp;
                    if (real_parameters[1].type is ICompiledGenericTypeInstance)
                        t2 = helper.GetTypeReference(real_parameters[1].type).tp;
                    Mono.Cecil.MethodReference mi = null;
                    var eq_members = mb.ImportReference(ctn1.compiled_type).Resolve()
                        .GetMethods()
                        .Where(item => item.IsPublic && !item.IsStatic && item.Name == "Equals");
                    bool value_type_eq = false;
                    foreach (var member in eq_members)
                        if (mi == null)
                            mi = member;
                        else if (member.Parameters[0].ParameterType.IsValueType)
                        {
                            mi = member;
                            value_type_eq = true;
                        }
                        
                    if (mi != null)
                    {
                        
                        real_parameters[0].visit(this);
                        if (value_type_eq)
                        {
                            var lb = new Mono.Cecil.Cil.VariableDefinition(t1);
                            il.Body.Variables.Add(lb);
                            il.Emit(OpCodes.Stloc, lb);
                            il.Emit(OpCodes.Ldloca, lb);
                            real_parameters[1].visit(this);
                        }
                        else
                        {
                            il.Emit(OpCodes.Box, t1);
                            real_parameters[1].visit(this);
                            il.Emit(OpCodes.Box, t2);
                            
                        }

                        il.Emit(OpCodes.Call, mi);
                        if (ft == basic_function_type.objnoteq)
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        return;
                    }
                    
                }
                
                switch (ft)
                {
                    case basic_function_type.booland:
                        ConvertSokrAndExpression(value);//сокращенное вычисление and
                        return;
                    case basic_function_type.boolor:
                        ConvertSokrOrExpression(value);//сокращенное вычисление or
                        return;
                    case basic_function_type.iassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.bassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.lassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.fassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.dassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.uiassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.usassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.ulassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.sassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.sbassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.boolassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.charassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.objassign: ConvertAssignExpr(real_parameters[0], real_parameters[1]); return;
                    case basic_function_type.iinc:
                    case basic_function_type.binc:
                    case basic_function_type.sinc:
                    case basic_function_type.linc:
                    case basic_function_type.uiinc:
                    case basic_function_type.sbinc:
                    case basic_function_type.usinc:
                    case basic_function_type.ulinc:
                    case basic_function_type.boolinc:
                    case basic_function_type.cinc: ConvertInc(real_parameters[0]); return;
                    case basic_function_type.idec:
                    case basic_function_type.bdec:
                    case basic_function_type.sdec:
                    case basic_function_type.ldec:
                    case basic_function_type.uidec:
                    case basic_function_type.sbdec:
                    case basic_function_type.usdec:
                    case basic_function_type.uldec:
                    case basic_function_type.booldec:
                    case basic_function_type.cdec: ConvertDec(real_parameters[0]); return;
                }
                if (real_parameters.Length > 1)
                {
                    if (real_parameters[0].type.is_nullable_type && real_parameters[1] is INullConstantNode)
                    {
                        bool tmp = is_dot_expr;
                        if (real_parameters[0] is IDefaultOperatorNode)
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                        }
                        else
                        {
                            TypeInfo ti = helper.GetTypeReference(real_parameters[0].type);

                            real_parameters[0].visit(this);
                            Mono.Cecil.Cil.VariableDefinition tmp_lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                            il.Body.Variables.Add(tmp_lb);
							il.Emit(OpCodes.Stloc, tmp_lb);
                            il.Emit(OpCodes.Ldloca, tmp_lb);

                            Mono.Cecil.MethodReference mi = null;
                            mi = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti.tp);
                            il.Emit(OpCodes.Call, mi);
                        }
                        
                        if (ft == basic_function_type.objeq)
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }

                        return;
                    }
                    else if (real_parameters[1].type.is_nullable_type && real_parameters[0] is INullConstantNode)
                    {
                        bool tmp = is_dot_expr;
                        if (real_parameters[1] is IDefaultOperatorNode)
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                        }
                        else
                        {
                            TypeInfo ti = helper.GetTypeReference(real_parameters[1].type);
                            real_parameters[1].visit(this);
                            Mono.Cecil.Cil.VariableDefinition tmp_lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
                            il.Body.Variables.Add(tmp_lb);
							il.Emit(OpCodes.Stloc, tmp_lb);
                            il.Emit(OpCodes.Ldloca, tmp_lb);
                            Mono.Cecil.MethodReference mi = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti.tp);
                            il.Emit(OpCodes.Call, mi);
                        }
                        if (ft == basic_function_type.objeq)
                        {
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        return;
                    }
                    else if (real_parameters[0].type.is_nullable_type && real_parameters[1].type.is_nullable_type)
                    {
                        Mono.Cecil.MethodReference mi_left = null;
                        Mono.Cecil.MethodReference mi_right = null;
                        TypeInfo ti_left = helper.GetTypeReference(real_parameters[0].type);
                        TypeInfo ti_right = helper.GetTypeReference(real_parameters[1].type);
                        Instruction lb_false = il.Create(OpCodes.Nop);
                        Instruction lb_true = il.Create(OpCodes.Nop);
                        Instruction lb_end = il.Create(OpCodes.Nop);
                        Instruction lb_common = il.Create(OpCodes.Nop);
                        Mono.Cecil.Cil.VariableDefinition lb_left = null;
                        Mono.Cecil.Cil.VariableDefinition lb_right = null;
                        if (!(real_parameters[0] is IDefaultOperatorNode) && !(real_parameters[1] is IDefaultOperatorNode))
                        {
                            //is_dot_expr = true;
                            lb_left = new Mono.Cecil.Cil.VariableDefinition(ti_left.tp);
                            il.Body.Variables.Add(lb_left);
                            lb_right = new Mono.Cecil.Cil.VariableDefinition(ti_right.tp);
                            il.Body.Variables.Add(lb_right);
                            real_parameters[0].visit(this);
                            il.Emit(OpCodes.Stloc, lb_left);
                            il.Emit(OpCodes.Ldloca, lb_left);
                            mi_left = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_left.tp);
                            il.Emit(OpCodes.Call, mi_left);
                            Mono.Cecil.Cil.VariableDefinition tmp_lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Boolean);
                            il.Body.Variables.Add(tmp_lb);
                            il.Emit(OpCodes.Stloc, tmp_lb);
                            il.Emit(OpCodes.Ldloc, tmp_lb);
                            //is_dot_expr = true;
                            real_parameters[1].visit(this);
                            il.Emit(OpCodes.Stloc, lb_right);
                            il.Emit(OpCodes.Ldloca, lb_right);
                            mi_right = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_right.tp);

                            il.Emit(OpCodes.Call, mi_right);
                            if (value.basic_function.basic_function_type == basic_function_type.objnoteq)
                            {
                                il.Emit(OpCodes.Ceq);
                                il.Emit(OpCodes.Ldc_I4_0);
                                il.Emit(OpCodes.Ceq);
                                il.Emit(OpCodes.Brtrue, lb_true);
                                il.Emit(OpCodes.Br, lb_common);
                                il.Append(lb_true);
                                il.Emit(OpCodes.Ldc_I4_1);
                                il.Emit(OpCodes.Br, lb_end);
                            }
                            else
                            {
                                il.Emit(OpCodes.Ceq);
                                il.Emit(OpCodes.Brfalse, lb_false);
                                il.Emit(OpCodes.Ldloc, tmp_lb);
                                il.Emit(OpCodes.Brtrue, lb_common);
                                il.Emit(OpCodes.Ldc_I4_1);
                                il.Emit(OpCodes.Br, lb_end);
                                il.Append(lb_false);
                                il.Emit(OpCodes.Ldc_I4_0);
                                il.Emit(OpCodes.Br, lb_end);
                            }

                        }
                        il.Append(lb_common);
                        if (real_parameters[0] is IDefaultOperatorNode)
                            il.Emit(OpCodes.Ldc_I4_0);
                        else
                        {
                            if (real_parameters[1] is IDefaultOperatorNode)
                            {
                                mi_left = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_left.tp);
                            }
                            else
                            {
                                mi_left = mb.ImportReference(TypeFactory.NullableGetValueOrDefaultMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_left.tp);
                            }
                            
                        }
                        
                        
                        
                        if (real_parameters[1] is IDefaultOperatorNode)
                            il.Emit(OpCodes.Ldc_I4_0);
                        else
                        {
                            if (real_parameters[0] is IDefaultOperatorNode)
                            {
                                mi_right = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_right.tp);
                            }
                            else
                            {
                                mi_right = mb.ImportReference(TypeFactory.NullableGetValueOrDefaultMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)ti_right.tp);
                            }
                            
                        }

                        if (!(real_parameters[0] is IDefaultOperatorNode))
                        {
                            if (lb_left != null)
                            {
                                il.Emit(OpCodes.Ldloca, lb_left);
                                il.Emit(OpCodes.Call, mi_left);
                            }
                            else
                            {
                                is_dot_expr = true;
                                real_parameters[0].visit(this);
                                il.Emit(OpCodes.Call, mi_left);
                            }
                        }
                        if (!(real_parameters[1] is IDefaultOperatorNode))
                        {
                            if (lb_right != null)
                            {
                                il.Emit(OpCodes.Ldloca, lb_right);
                                il.Emit(OpCodes.Call, mi_right);
                            }
                            else
                            {
                                is_dot_expr = true;
                                real_parameters[1].visit(this);
                                il.Emit(OpCodes.Call, mi_right);
                            }
                            
                        }
                        Mono.Cecil.MethodReference eq_mi = null;
                        if (real_parameters[0].type is IGenericTypeInstance)
                        {
                            var ctn = (real_parameters[0].type as IGenericTypeInstance).generic_parameters[0] as ICommonTypeNode;

                            if (ctn != null)
                            {
                                foreach (ICommonMethodNode cmn in ctn.methods)
                                    if ((value.basic_function.basic_function_type == basic_function_type.objnoteq? cmn.name == "op_Inequality" || cmn.name == "<>":cmn.name == "op_Equality" || cmn.name == "=") && cmn.parameters.Length == 2 && cmn.parameters[0].type == ctn && cmn.parameters[1].type == ctn)
                                    {
                                        eq_mi = helper.GetMethod(cmn).mi;
                                        break;
                                    }
                            }
                        }
                        else if (real_parameters[0].type is ICompiledTypeNode)
                        {
                            var ct = (Mono.Cecil.GenericInstanceType)mb.ImportReference((real_parameters[0].type as ICompiledTypeNode).compiled_type);
                            var t = ct.GenericArguments[0];
                            foreach (Mono.Cecil.MethodReference mi in t.Resolve().GetMethods().Where(item => item.IsPublic && item.IsStatic))
                            {
                                if ((value.basic_function.basic_function_type == basic_function_type.objnoteq ? mi.Name == "op_Inequality" : mi.Name == "op_Equality") && mi.Parameters.Count == 2 && mi.Parameters[0].ParameterType.FullName == t.FullName && mi.Parameters[1].ParameterType.FullName == t.FullName)
                                {
                                    eq_mi = mi;
                                    break;
                                }
                            }
                        }
                        
                        if (eq_mi != null)
                            il.Emit(OpCodes.Call, eq_mi);
                        else
                            EmitOperator(value);
                        il.Append(lb_end);

                        is_dot_expr = tmp_dot;
                        if (tmp_dot)
                        {
                            is_dot_expr = tmp_dot;
                            NETGeneratorTools.CreateLocalAndLoad(il, helper.GetTypeReference(value.type).tp);
                        }
                        return;
                    }
                    else if (real_parameters[0].type.is_generic_parameter && real_parameters[1] is INullConstantNode)
                    { 
                        real_parameters[0].visit(this);
                        il.Emit(OpCodes.Box, helper.GetTypeReference(real_parameters[0].type).tp);
                        il.Emit(real_parameters[1].type is IRefTypeNode ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);
                        EmitOperator(value);
                        return;
                    }
                    else if (real_parameters[1].type.is_generic_parameter && real_parameters[0] is INullConstantNode)
                    {
                        il.Emit(real_parameters[0].type is IRefTypeNode ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);
                        real_parameters[1].visit(this);
                        il.Emit(OpCodes.Box, helper.GetTypeReference(real_parameters[1].type).tp);
                        EmitOperator(value);
                        return;
                    }
                }
                real_parameters[0].visit(this);
                if (value.basic_function.basic_function_type == basic_function_type.uimul)
                    il.Emit(OpCodes.Conv_I8);
                if (real_parameters.Length > 1)
                {
                    real_parameters[1].visit(this);
                    if (value.basic_function.basic_function_type == basic_function_type.uimul)
                        il.Emit(OpCodes.Conv_I8);
                }
                    
                EmitOperator(value);//кладем соотв. команду
                if (tmp_dot)
                {
                    is_dot_expr = tmp_dot;
                    NETGeneratorTools.CreateLocalAndLoad(il, helper.GetTypeReference(value.type).tp);
                }
            }
        }

        private void ConvertSokrAndExpression(IBasicFunctionCallNode expr)
        {
            Instruction lb1 = il.Create(OpCodes.Nop);
            Instruction lb2 = il.Create(OpCodes.Nop);
            IExpressionNode[] real_parameters = expr.real_parameters;
            real_parameters[0].visit(this);
            il.Emit(OpCodes.Brfalse, lb1);
            il.Emit(OpCodes.Ldc_I4_1);
            real_parameters[1].visit(this);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Br, lb2);
            il.Append(lb1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Append(lb2);
        }

        private void ConvertSokrOrExpression(IBasicFunctionCallNode expr)
        {
            Instruction lb1 = il.Create(OpCodes.Nop);
            Instruction lb2 = il.Create(OpCodes.Nop);
            IExpressionNode[] real_parameters = expr.real_parameters;
            real_parameters[0].visit(this);
            il.Emit(OpCodes.Brtrue, lb1);
            il.Emit(OpCodes.Ldc_I4_0);
            real_parameters[1].visit(this);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Br, lb2);
            il.Append(lb1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Append(lb2);
        }

        protected virtual void EmitOperator(IBasicFunctionCallNode fn)
        {
            basic_function_type ft = fn.basic_function.basic_function_type;
            switch (ft)
            {
                case basic_function_type.none: return;

                case basic_function_type.iadd:
                case basic_function_type.badd:
                case basic_function_type.sadd:
                case basic_function_type.ladd:
                case basic_function_type.fadd:
                case basic_function_type.sbadd:
                case basic_function_type.usadd:
                case basic_function_type.uladd:
                case basic_function_type.dadd: il.Emit(OpCodes.Add); break;
                case basic_function_type.uiadd:
                    //il.Emit(OpCodes.Conv_U8);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Conv_I8);
                    break;

                case basic_function_type.isub:
                case basic_function_type.bsub:
                case basic_function_type.ssub:
                case basic_function_type.lsub:
                case basic_function_type.fsub:
                //case basic_function_type.uisub:
                case basic_function_type.sbsub:
                case basic_function_type.ussub:
                case basic_function_type.ulsub:
                case basic_function_type.dsub: il.Emit(OpCodes.Sub); break;
                case basic_function_type.uisub: il.Emit(OpCodes.Sub); il.Emit(OpCodes.Conv_I8); break;

                case basic_function_type.imul:
                case basic_function_type.bmul:
                case basic_function_type.smul:
                case basic_function_type.lmul:
                case basic_function_type.fmul:
                //case basic_function_type.uimul:
                case basic_function_type.sbmul:
                case basic_function_type.usmul:
                case basic_function_type.ulmul:
                case basic_function_type.dmul: il.Emit(OpCodes.Mul); break;
                case basic_function_type.uimul: il.Emit(OpCodes.Mul); il.Emit(OpCodes.Conv_I8); break;

                case basic_function_type.idiv:
                case basic_function_type.bdiv:
                case basic_function_type.sdiv:
                case basic_function_type.ldiv:
                case basic_function_type.fdiv:
                //case basic_function_type.uidiv:
                case basic_function_type.sbdiv:
                case basic_function_type.usdiv:

                case basic_function_type.ddiv: il.Emit(OpCodes.Div); break;
                case basic_function_type.uidiv: il.Emit(OpCodes.Div_Un); il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.uldiv: il.Emit(OpCodes.Div_Un); break;

                case basic_function_type.imod:
                case basic_function_type.bmod:
                case basic_function_type.smod:
                //case basic_function_type.uimod:
                case basic_function_type.sbmod:
                case basic_function_type.usmod:

                case basic_function_type.lmod: il.Emit(OpCodes.Rem); break;
                case basic_function_type.uimod: il.Emit(OpCodes.Rem_Un); il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.ulmod: il.Emit(OpCodes.Rem_Un); break;

                case basic_function_type.isinc:
                case basic_function_type.bsinc:
                case basic_function_type.sbsinc:
                case basic_function_type.ssinc:
                case basic_function_type.ussinc:
                case basic_function_type.uisinc:
                case basic_function_type.ulsinc: il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); break;
                case basic_function_type.lsinc: il.Emit(OpCodes.Ldc_I8, 1); il.Emit(OpCodes.Add); break;

                case basic_function_type.isdec:
                case basic_function_type.bsdec:
                case basic_function_type.sbsdec:
                case basic_function_type.ssdec:
                case basic_function_type.ussdec:
                case basic_function_type.uisdec:
                case basic_function_type.ulsdec: il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); break;
                case basic_function_type.lsdec: il.Emit(OpCodes.Ldc_I8, 1); il.Emit(OpCodes.Sub); break;

                case basic_function_type.inot:
                case basic_function_type.bnot:
                case basic_function_type.snot:
                case basic_function_type.uinot:
                case basic_function_type.sbnot:
                case basic_function_type.usnot:
                case basic_function_type.ulnot:
                case basic_function_type.lnot: il.Emit(OpCodes.Not); break;
                case basic_function_type.boolsdec:
                case basic_function_type.boolsinc:
                case basic_function_type.boolnot: il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;

                case basic_function_type.ishl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.ishr: il.Emit(OpCodes.Shr); break;
                case basic_function_type.bshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.bshr: il.Emit(OpCodes.Shr_Un); break;
                case basic_function_type.sshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.sshr: il.Emit(OpCodes.Shr); break;
                case basic_function_type.lshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.lshr: il.Emit(OpCodes.Shr); break;
                case basic_function_type.uishl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.sbshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.usshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.ulshl: il.Emit(OpCodes.Shl); break;
                case basic_function_type.uishr: il.Emit(OpCodes.Shr_Un); break;
                case basic_function_type.sbshr: il.Emit(OpCodes.Shr); break;
                case basic_function_type.usshr: il.Emit(OpCodes.Shr_Un); break;
                case basic_function_type.ulshr: il.Emit(OpCodes.Shr_Un); break;

                case basic_function_type.ieq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.inoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.igr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.igreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ism: il.Emit(OpCodes.Clt); break;
                case basic_function_type.ismeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.seq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.snoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.sgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.sgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ssm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.ssmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.beq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.bnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.bgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.bgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.bsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.bsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;


                case basic_function_type.leq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.lnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.lgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.lgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.lsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.lsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.uieq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.uinoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.uigr: il.Emit(OpCodes.Cgt_Un); break;
                case basic_function_type.uigreq: il.Emit(OpCodes.Clt_Un); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.uism: il.Emit(OpCodes.Clt_Un); break;
                case basic_function_type.uismeq: il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.sbeq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.sbnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.sbgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.sbgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.sbsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.sbsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.useq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.usnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.usgr: il.Emit(OpCodes.Cgt_Un); break;
                case basic_function_type.usgreq: il.Emit(OpCodes.Clt_Un); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ussm: il.Emit(OpCodes.Clt_Un); break;
                case basic_function_type.ussmeq: il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.uleq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ulnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ulgr: il.Emit(OpCodes.Cgt_Un); break;
                case basic_function_type.ulgreq: il.Emit(OpCodes.Clt_Un); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.ulsm: il.Emit(OpCodes.Clt_Un); break;
                case basic_function_type.ulsmeq: il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.booleq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.boolnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.boolgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.boolgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.boolsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.boolsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;


                case basic_function_type.feq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.fnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.fgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.fgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.fsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.fsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;


                case basic_function_type.deq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.dnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.dgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.dgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.dsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.dsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;


                case basic_function_type.chareq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.charnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.chargr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.chargreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.charsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.charsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.objeq: il.Emit(OpCodes.Ceq); break;
                case basic_function_type.objnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;

                case basic_function_type.band: il.Emit(OpCodes.And); break;
                case basic_function_type.bor: il.Emit(OpCodes.Or); break;
                case basic_function_type.bxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.iand: il.Emit(OpCodes.And); break;
                case basic_function_type.ior: il.Emit(OpCodes.Or); break;
                case basic_function_type.ixor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.enumsand: il.Emit(OpCodes.And); break;
                case basic_function_type.enumsor: il.Emit(OpCodes.Or); break;
                case basic_function_type.enumsxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.land: il.Emit(OpCodes.And); break;
                case basic_function_type.lor: il.Emit(OpCodes.Or); break;
                case basic_function_type.lxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.sand: il.Emit(OpCodes.And); break;
                case basic_function_type.sor: il.Emit(OpCodes.Or); break;
                case basic_function_type.sxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.uiand: il.Emit(OpCodes.And); break;
                case basic_function_type.uior: il.Emit(OpCodes.Or); break;
                case basic_function_type.uixor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.sband: il.Emit(OpCodes.And); break;
                case basic_function_type.sbor: il.Emit(OpCodes.Or); break;
                case basic_function_type.sbxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.usand: il.Emit(OpCodes.And); break;
                case basic_function_type.usor: il.Emit(OpCodes.Or); break;
                case basic_function_type.usxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.uland: il.Emit(OpCodes.And); break;
                case basic_function_type.ulor: il.Emit(OpCodes.Or); break;
                case basic_function_type.ulxor: il.Emit(OpCodes.Xor); break;

                case basic_function_type.booland: il.Emit(OpCodes.And); break;
                case basic_function_type.boolor: il.Emit(OpCodes.Or); break;
                case basic_function_type.boolxor: il.Emit(OpCodes.Xor); break;
                //case basic_function_type.chareq: il.Emit(OpCodes.Ceq); break;
                //case basic_function_type.charnoteq: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.enumgr: il.Emit(OpCodes.Cgt); break;
                case basic_function_type.enumgreq: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case basic_function_type.enumsm: il.Emit(OpCodes.Clt); break;
                case basic_function_type.enumsmeq: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq); break;

                case basic_function_type.iunmin:
                case basic_function_type.bunmin:
                case basic_function_type.sunmin:
                case basic_function_type.funmin:
                case basic_function_type.dunmin:
                //case basic_function_type.uiunmin:
                case basic_function_type.sbunmin:
                case basic_function_type.usunmin:
                //case basic_function_type.ulunmin:
                case basic_function_type.lunmin: il.Emit(OpCodes.Neg); break;
                case basic_function_type.uiunmin: il.Emit(OpCodes.Neg); il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.chartoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.chartous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.chartoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.chartol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.chartoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.chartof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.chartod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.chartob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.chartosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.chartos: il.Emit(OpCodes.Conv_I2); break;

                case basic_function_type.itod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.itol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.itof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.itob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.itosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.itos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.itous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.itoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.itoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.itochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.btos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.btous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.btoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.btoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.btol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.btoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.btof: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.btod: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.btosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.btochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.sbtos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.sbtoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.sbtol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.sbtof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.sbtod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.sbtob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.sbtous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.sbtoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.sbtoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.sbtochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.stoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.stol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.stof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.stod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.stob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.stosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.stous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.stoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.stoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.stochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.ustoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.ustoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.ustol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.ustoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.ustof: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.ustod: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.ustob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.ustosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.ustos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.ustochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.uitoi: il.Emit(OpCodes.Conv_I4); break;
                //case basic_function_type.ustoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.uitol: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.uitoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.uitof: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.uitod: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.uitob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.uitosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.uitos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.uitous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.uitochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.ultof: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.ultod: il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.ultob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.ultosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.ultos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.ultous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.ultoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.ultoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.ultol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.ultochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.ltof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.ltod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.ltob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.ltosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.ltos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.ltous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.ltoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.ltoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.ltoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.ltochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.ftod: il.Emit(OpCodes.Conv_R8); break;
                case basic_function_type.ftob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.ftosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.ftos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.ftous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.ftoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.ftoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.ftol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.ftoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.ftochar: il.Emit(OpCodes.Conv_U2); break;

                case basic_function_type.dtob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.dtosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.dtos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.dtous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.dtoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.dtoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.dtol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.dtoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.dtof: il.Emit(OpCodes.Conv_R4); break;
                case basic_function_type.dtochar: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.booltoi: il.Emit(OpCodes.Conv_I4); break;
                case basic_function_type.booltob: il.Emit(OpCodes.Conv_U1); break;
                case basic_function_type.booltosb: il.Emit(OpCodes.Conv_I1); break;
                case basic_function_type.booltos: il.Emit(OpCodes.Conv_I2); break;
                case basic_function_type.booltous: il.Emit(OpCodes.Conv_U2); break;
                case basic_function_type.booltoui: il.Emit(OpCodes.Conv_U4); break;
                case basic_function_type.booltol: il.Emit(OpCodes.Conv_I8); break;
                case basic_function_type.booltoul: il.Emit(OpCodes.Conv_U8); break;
                case basic_function_type.ltop: il.Emit(OpCodes.Conv_I); break;
                case basic_function_type.ptol: il.Emit(OpCodes.Conv_I8); break;

                case basic_function_type.objtoobj:
                    {
                        //(ssyy) Вставил 15.05.08
                        Mono.Cecil.TypeReference from_val_type = null;
                        IExpressionNode par0 = fn.real_parameters[0];
                        ITypeNode tn = par0.type; 
                        if (par0.conversion_type != null)
                            tn = par0.conversion_type;
                        if (!(par0 is SemanticTree.INullConstantNode) && (tn.is_value_type || tn.is_generic_parameter))
                        {
                            from_val_type = helper.GetTypeReference(tn).tp;
                        }
                        Mono.Cecil.TypeReference t = helper.GetTypeReference(fn.type).tp;
                        if (!fn.type.IsDelegate)
                            NETGeneratorTools.PushCast(il, t, from_val_type);
                        break;
                    }

            }
        }



        public override void visit(SemanticTree.IFunctionCallNode value)
        {

        }

        public override void visit(SemanticTree.IExpressionNode value)
        {

        }

        public override void visit(SemanticTree.IStatementNode value)
        {

        }

        public override void visit(SemanticTree.ICompiledTypeNode value)
        {

        }

        //перевод оболочки для массива
        public void ConvertArrayWrapperType(SemanticTree.ICommonTypeNode value)
        {
            ISimpleArrayNode arrt = value.fields[0].type as ISimpleArrayNode;
            TypeInfo elem_ti = helper.GetTypeReference(arrt.element_type);
            if (elem_ti == null)
            {
                ConvertTypeHeader((ICommonTypeNode)arrt.element_type);
            }
            else
                if (elem_ti.is_arr && elem_ti.def_cnstr == null)
                    ConvertArrayWrapperType((ICommonTypeNode)arrt.element_type);
            TypeInfo ti = helper.GetTypeReference(value);
            if (ti.def_cnstr != null) return;
            ti.is_arr = true;
            Mono.Cecil.TypeDefinition tb = (Mono.Cecil.TypeDefinition)ti.tp;
            TypeInfo tmp_ti = cur_ti;
            cur_ti = ti;
            //TypeBuilder tb = (TypeBuilder)helper.GetTypeBuilder(value);
            //это метод для выделения памяти под массивы
            Mono.Cecil.MethodDefinition mb = new Mono.Cecil.MethodDefinition("$Init$", MethodAttributes.Private, this.mb.TypeSystem.Void);
            tb.Methods.Add(mb);
            ti.init_meth = mb;
            Mono.Cecil.MethodDefinition hndl_mb = null;
            Mono.Cecil.TypeDefinition tmp = cur_type;
            cur_type = tb;

            foreach (ICommonClassFieldNode fld in value.fields)
                fld.visit(this);
            foreach (ICommonPropertyNode prop in value.properties)
                prop.visit(this);
            foreach (ICommonMethodNode meth in value.methods)
                ConvertMethodHeader(meth);
            //foreach (ICommonMethodNode meth in value.methods)
            //	meth.visit(this);
            foreach (IClassConstantDefinitionNode constant in value.constants)
                constant.visit(this);


            cur_type = tmp;
            mb.Body.GetILProcessor().Emit(OpCodes.Ret);
            if (hndl_mb != null)
                hndl_mb.Body.GetILProcessor().Emit(OpCodes.Ret);
            cur_ti = tmp_ti;
        }

        //перевод реализации типа
        public override void visit(SemanticTree.ICommonTypeNode value)
        {
            if (value is ISimpleArrayNode || value.type_special_kind == type_special_kind.array_kind) return;
            MakeAttribute(value);
            TypeInfo ti = helper.GetTypeReference(value);
            if (ti.tp.Resolve().IsEnum || !ti.tp.IsDefinition) return;
            Mono.Cecil.TypeDefinition tb = (Mono.Cecil.TypeDefinition)ti.tp;
            TypeInfo tmp_ti = cur_ti;
            cur_ti = ti;
			Mono.Cecil.TypeDefinition tmp = cur_type;
            cur_type = tb;

            foreach (ICommonMethodNode meth in value.methods)
                meth.visit(this);
            cur_type = tmp;
            cur_ti = tmp_ti;
        }

        public override void visit(SemanticTree.IBasicTypeNode value)
        {

        }

        public override void visit(SemanticTree.ISimpleArrayNode value)
        {

        }

        //доступ к элементам массива
        public override void visit(SemanticTree.ISimpleArrayIndexingNode value)
        {
            //Console.WriteLine(value.array.type);
            bool temp_is_addr = is_addr;
            bool temp_is_dot_expr = is_dot_expr;
            is_addr = false;
            is_dot_expr = false;
            TypeInfo ti = helper.GetTypeReference(value.array.type);
            Mono.Cecil.Cil.VariableDefinition tmp_current_index_lb = current_index_lb;
            current_index_lb = null;
            value.array.visit(this);
            current_index_lb = tmp_current_index_lb;
            bool string_getter = temp_is_addr && ti.tp.FullName == mb.TypeSystem.String.FullName;
            Mono.Cecil.Cil.VariableDefinition pin_lb = null;
            var indices = value.indices;
            if (string_getter)
            {
                pin_lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.String.MakePinnedType());
                il.Body.Variables.Add(pin_lb);
                Mono.Cecil.Cil.VariableDefinition chr_ptr_lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Char.MakePointerType());
                il.Body.Variables.Add(chr_ptr_lb);
				//pinned_handle = il.DeclareLocal(TypeFactory.GCHandleType);
				Instruction false_lbl = il.Create(OpCodes.Nop);
                if (copy_string)
                {
                    /*var mcall = new TreeRealization.compiled_static_method_call(TreeRealization.compiled_function_node.get_compiled_method(TypeFactory.StringCopyMethod), null);
                    mcall.parameters.AddElement(value.array as TreeRealization.expression_node);
                    ConvertAssignExpr(value.array, mcall);*/
                    if (value.array is ILocalBlockVariableReferenceNode || value.array is ILocalVariableReferenceNode || value.array is INamespaceVariableReferenceNode)
                    {
                        il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.StringCopyMethod));
                        if (value.array is ILocalVariableReferenceNode)
                        {
                            var vi = helper.GetVariable((value.array as ILocalVariableReferenceNode).Variable);
                            il.Emit(OpCodes.Stloc, vi.lb);
                            il.Emit(OpCodes.Ldloc, vi.lb);
                        }
                        else if (value.array is ILocalBlockVariableReferenceNode)
                        {
                            var vi = helper.GetVariable((value.array as ILocalBlockVariableReferenceNode).Variable);
                            il.Emit(OpCodes.Stloc, vi.lb);
                            il.Emit(OpCodes.Ldloc, vi.lb);
                        }
                        else if (value.array is INamespaceVariableReferenceNode)
                        {
                            VarInfo vi = helper.GetVariable((value.array as INamespaceVariableReferenceNode).Variable);
                            if (vi == null)
                            {
                                ConvertGlobalVariable((value.array as INamespaceVariableReferenceNode).variable);
                                vi = helper.GetVariable((value.array as INamespaceVariableReferenceNode).Variable);
                            }
                            il.Emit(OpCodes.Stsfld, vi.fb);
                            il.Emit(OpCodes.Ldsfld, vi.fb);
                        }

                    }

                    copy_string = false;
                }
                il.Emit(OpCodes.Stloc, pin_lb);
                /*il.Emit(OpCodes.Ldloc, pin_lb);
                il.Emit(OpCodes.Ldc_I4, (int)GCHandleType.Pinned);
                il.Emit(OpCodes.Call, TypeFactory.GCHandleAllocPinned);
                il.Emit(OpCodes.Stloc, pinned_handle);*/
                il.Emit(OpCodes.Ldloc, pin_lb);
                il.Emit(OpCodes.Conv_I);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, false_lbl);
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.OffsetToStringDataProperty));
                il.Emit(OpCodes.Add);
                il.Append(false_lbl);
                il.Emit(OpCodes.Stloc, chr_ptr_lb);
                il.Emit(OpCodes.Ldloc, chr_ptr_lb);
            }
            //посещаем индекс
            Mono.Cecil.MethodReference get_meth = null;
            Mono.Cecil.MethodReference addr_meth = null;
            ISimpleArrayNode arr_type = value.array.type as ISimpleArrayNode;
            TypeInfo elem_ti = null;
            Mono.Cecil.TypeReference elem_type = null;
            if (arr_type != null)
            {
                elem_ti = helper.GetTypeReference(arr_type.element_type);
                elem_type = elem_ti.tp;
            }
            else
                elem_type = ((Mono.Cecil.ArrayType)ti.tp).ElementType;
            if (indices == null)
            {
                if (current_index_lb == null)
                    value.index.visit(this);
                else
                {
                    il.Emit(OpCodes.Ldloc, current_index_lb);
                    current_index_lb = null;
                }
                if (string_getter)
                {
                    Instruction except_lbl = il.Create(OpCodes.Nop);
                    Instruction ok_lbl = il.Create(OpCodes.Nop);
                    Mono.Cecil.Cil.VariableDefinition ind_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.index.type).tp);
                    il.Body.Variables.Add(ind_lb);
                    if (value.array.type is IShortStringTypeNode)
                    {
                        il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Sub);
                    }
                    il.Emit(OpCodes.Stloc, ind_lb);
                    il.Emit(OpCodes.Ldloc, ind_lb);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Blt, except_lbl);
                    il.Emit(OpCodes.Ldloc, ind_lb);
                    il.Emit(OpCodes.Ldloc, pin_lb);
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.StringLengthMethod));
                    il.Emit(OpCodes.Bge_S, except_lbl);
                    il.Emit(OpCodes.Ldloc, ind_lb);
                    il.Emit(OpCodes.Ldc_I4_2);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Conv_I);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Br, ok_lbl);
                    il.Append(except_lbl);
                    il.Emit(OpCodes.Newobj, mb.ImportReference(TypeFactory.IndexOutOfRangeCtor));
                    il.Emit(OpCodes.Throw);
                    il.Append(ok_lbl);
                }
            }
            else
            {
                List<Mono.Cecil.TypeReference> lst = new List<Mono.Cecil.TypeReference>();
                for (int i = 0; i < value.indices.Length; i++)
                    lst.Add(mb.TypeSystem.Int32);
                get_meth = new Mono.Cecil.MethodReference("Get", elem_type, ti.tp);
                get_meth.HasThis = true;
                foreach (var paramType in lst)
                    get_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));
                addr_meth = new Mono.Cecil.MethodReference("Address", elem_type.MakeByReferenceType(), ti.tp);
                addr_meth.HasThis = true;
                foreach (var paramType in lst)
                    addr_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));

                for (int i = 0; i < indices.Length; i++)
                    indices[i].visit(this);
            }

            if (temp_is_addr)
            {
                if (value.indices == null)
                {
                    if (!string_getter)
                        il.Emit(OpCodes.Ldelema, elem_type);
                }
                else
                    il.Emit(OpCodes.Call, addr_meth);
            }
            else
                if (temp_is_dot_expr)
            {
                if (elem_type.IsGenericParameter)
                {
                    if (value.array.type.element_type.is_generic_parameter && value.array.type.element_type.base_type != null && value.array.type.element_type.base_type.is_class && value.array.type.element_type.base_type.base_type != null)
                    {
                        if (indices == null)
                            il.Emit(OpCodes.Ldelem_Ref);
                        else
                            il.Emit(OpCodes.Call, get_meth);
                    }
                    else
                    {
                        if (indices == null)
                            il.Emit(OpCodes.Ldelema, elem_type);
                        else
                            il.Emit(OpCodes.Call, addr_meth);
                    }
                        
                }
                else if (elem_type.IsValueType == true)
                {
                    if (indices == null)
                        il.Emit(OpCodes.Ldelema, elem_type);
                    else
                        il.Emit(OpCodes.Call, addr_meth);
                }
                else if (elem_type.IsPointer)
                {
                    if (indices == null)
                        il.Emit(OpCodes.Ldelem_I);
                    else
                        il.Emit(OpCodes.Call, addr_meth);
                }
                else
                    if (indices == null)
                    il.Emit(OpCodes.Ldelem_Ref);
                else
                    il.Emit(OpCodes.Call, get_meth);

            }
            else
            {
                if (indices == null)
                    NETGeneratorTools.PushLdelem(il, elem_type, true);
                else
                    il.Emit(OpCodes.Call, get_meth);
            }
            is_addr = temp_is_addr;
            is_dot_expr = temp_is_dot_expr;
            //if (pinned_handle != null)
            //    pinned_variables.Add(pinned_handle);

        }

        public override void visit(SemanticTree.ITypeNode value)
        {

        }

        public override void visit(SemanticTree.IDefinitionNode value)
        {

        }

        public override void visit(SemanticTree.ISemanticNode value)
        {

        }

        public override void visit(SemanticTree.IReturnNode value)
        {
            if (save_debug_info)
                MarkSequencePoint(value.Location);

            OptMakeExitLabel();

            //(ssyy) Проверка на конструктор
            if (value.return_value != null)
            {
                if (!is_constructor)
                {
                    value.return_value.visit(this);
                }
            }
            il.Emit(OpCodes.Ret);
        }

        //строковая константа
        public override void visit(SemanticTree.IStringConstantNode value)
        {
            il.Emit(OpCodes.Ldstr, value.constant_value);
        }

        //реализация this
        public override void visit(SemanticTree.IThisNode value)
        {
            il.Emit(OpCodes.Ldarg_0);
            if (value.type.is_value_type && !is_dot_expr && !is_addr)
            {
                il.Emit(OpCodes.Ldobj, helper.GetTypeReference(value.type).tp);
            }
        }

        private void PushObjectCommand(SemanticTree.IFunctionCallNode ifc)
        {
            SemanticTree.ICommonNamespaceFunctionCallNode cncall = ifc as SemanticTree.ICommonNamespaceFunctionCallNode;
            if (cncall != null)
            {
                il.Emit(OpCodes.Ldnull);
                return;
            }
            SemanticTree.ICommonMethodCallNode cmcall = ifc as SemanticTree.ICommonMethodCallNode;
            if (cmcall != null)
            {
                Mono.Cecil.Cil.VariableDefinition memoized_lb = helper.GetLocalBuilderForExpression(cmcall.obj);
                if (memoized_lb != null)
                    il.Emit(OpCodes.Ldloc, memoized_lb);
                else
                    cmcall.obj.visit(this);
                if (cmcall.obj.type.is_value_type)
                    il.Emit(OpCodes.Box, helper.GetTypeReference(cmcall.obj.type).tp);
                else if (cmcall.obj.conversion_type != null && cmcall.obj.conversion_type.is_value_type)
                	il.Emit(OpCodes.Box, helper.GetTypeReference(cmcall.obj.conversion_type).tp);
                return;
            }
            SemanticTree.ICommonStaticMethodCallNode csmcall = ifc as SemanticTree.ICommonStaticMethodCallNode;
            if (csmcall != null)
            {
                il.Emit(OpCodes.Ldnull);
                return;
            }
            SemanticTree.ICompiledMethodCallNode cmccall = ifc as SemanticTree.ICompiledMethodCallNode;
            if (cmccall != null)
            {
                Mono.Cecil.Cil.VariableDefinition memoized_lb = helper.GetLocalBuilderForExpression(cmccall.obj);
                if (memoized_lb != null)
                    il.Emit(OpCodes.Ldloc, memoized_lb);
                else
                    cmccall.obj.visit(this);
                if (cmccall.obj.type.is_value_type)
                    il.Emit(OpCodes.Box, helper.GetTypeReference(cmccall.obj.type).tp);
                else if (cmccall.obj.conversion_type != null && cmccall.obj.conversion_type.is_value_type)
                	il.Emit(OpCodes.Box, helper.GetTypeReference(cmccall.obj.conversion_type).tp);
                return;
            }
            SemanticTree.ICompiledStaticMethodCallNode csmcall2 = ifc as SemanticTree.ICompiledStaticMethodCallNode;
            if (csmcall2 != null)
            {
                il.Emit(OpCodes.Ldnull);
                return;
            }
            SemanticTree.ICommonNestedInFunctionFunctionCallNode cnffcall = ifc as SemanticTree.ICommonNestedInFunctionFunctionCallNode;
            if (cnffcall != null)
            {
                //cnffcall.
                //TODO: Дописать код для этого случая.
                return;
            }
            return;
        }

        //Вызов конструктора параметра generic-типа
        public void ConvertGenericParamCtorCall(ICommonConstructorCall value)
        {
            Mono.Cecil.TypeReference gpar = helper.GetTypeReference(value.common_type).tp;
            Mono.Cecil.GenericInstanceMethod create_inst = new Mono.Cecil.GenericInstanceMethod(mb.ImportReference(TypeFactory.ActivatorCreateInstanceMethod));
            create_inst.GenericArguments.Add(gpar);

            il.Emit(OpCodes.Call, create_inst);
        }

        //вызов конструктора
        public override void visit(SemanticTree.ICommonConstructorCall value)
        {
            bool tmp_dot = is_dot_expr;
            //if (save_debug_info)
            //    MarkSequencePoint(value.Location);
            if (value.common_type.is_generic_parameter)
            {
                ConvertGenericParamCtorCall(value);
                return;
            }
            IExpressionNode[] real_parameters = value.real_parameters;
            IParameterNode[] parameters = value.static_method.parameters;
            MethInfo ci = helper.GetConstructor(value.static_method);
            Mono.Cecil.MethodReference cnstr = ci.cnstr;

            SemanticTree.IRuntimeManagedMethodBody irmmb = value.static_method.function_code as SemanticTree.IRuntimeManagedMethodBody;
            if (irmmb != null)
            {
                if (irmmb.runtime_statement_type == SemanticTree.runtime_statement_type.ctor_delegate)
                {
                    SemanticTree.IFunctionCallNode ifc = real_parameters[0] as SemanticTree.IFunctionCallNode;
                    Mono.Cecil.MethodReference mi = null;
                    ICompiledMethodCallNode icmcn = ifc as ICompiledMethodCallNode;
                    if (icmcn != null)
                    {
                        mi = mb.ImportReference(icmcn.compiled_method.method_info);
                    }
                    else
                    {
                        ICompiledStaticMethodCallNode icsmcn = ifc as ICompiledStaticMethodCallNode;
                        if (icsmcn != null)
                        {
                            mi = mb.ImportReference(icsmcn.static_method.method_info);
                        }
                        else
                        {
                            mi = helper.GetMethod(ifc.function).mi;
                        }
                    }
                    PushObjectCommand(ifc);
                    if (mi.Resolve().IsVirtual || mi.Resolve().IsAbstract)
                    {
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Ldvirtftn, mi);
                    }
                    else
                        il.Emit(OpCodes.Ldftn, mi);
                    il.Emit(OpCodes.Newobj, cnstr);
                    return;
                }
                return;
            }

            if (!value.new_obj_awaited())
            {
                il.Emit(OpCodes.Ldarg_0);
            }

            is_dot_expr = false;
            EmitArguments(parameters, real_parameters);
            if (value.new_obj_awaited())
            {
                il.Emit(OpCodes.Newobj, cnstr);
                var ti = helper.GetTypeReference(value.common_type);
                /*if (ti != null && ti.init_meth != null && value.common_type.is_value_type)
                {
                    LocalBuilder lb = il.DeclareLocal(ti.tp);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                    il.Emit(OpCodes.Call, ti.init_meth);
                    il.Emit(OpCodes.Ldloc, lb);
                }*/
            }
            else
            {
                il.Emit(OpCodes.Call, cnstr);
            }
            EmitFreePinnedVariables();
            if (tmp_dot == true)
            {
                //MethodInfo mi = value.compiled_method.method_info;
                if (cnstr.DeclaringType.IsValueType && !NETGeneratorTools.IsPointer(cnstr.DeclaringType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(cnstr.DeclaringType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
            }
            else
            {
                is_dot_expr = false;
            }
            if (init_call_awaited && !value.new_obj_awaited() && cnstr.DeclaringType.FullName != cur_type.FullName)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, cur_ti.init_meth);
                //throw new Exception(cnstr.DeclaringType.Name+"-"+cur_meth.DeclaringType.Name);
                init_call_awaited = false;
            }
        }

        //вызов откомпилированного конструктора
        public override void visit(SemanticTree.ICompiledConstructorCall value)
        {
            //if (save_debug_info) MarkSequencePoint(value.Location);
            bool tmp_dot = is_dot_expr;
            Mono.Cecil.MethodReference mi = null;
            IParameterNode[] parameters = value.constructor.parameters;
            IExpressionNode[] real_parameters = value.real_parameters;
            Mono.Cecil.TypeReference cons_type11 = mb.ImportReference(value.constructor.comprehensive_type.compiled_type);
            if (cons_type11.Resolve().BaseType?.FullName == TypeFactory.MulticastDelegateType.FullName)
            {
                SemanticTree.IFunctionCallNode ifc = real_parameters[0] as SemanticTree.IFunctionCallNode;
                ICompiledMethodCallNode icmcn = ifc as ICompiledMethodCallNode;
                if (icmcn != null)
                {
                    mi = mb.ImportReference(icmcn.compiled_method.method_info);
                }
                else
                {
                    ICompiledStaticMethodCallNode icsmcn = ifc as ICompiledStaticMethodCallNode;
                    if (icsmcn != null)
                    {
                        mi = mb.ImportReference(icsmcn.static_method.method_info);
                    }
                    else
                    {
                        var meth = helper.GetMethod(ifc.function);
                        mi = meth.mi;
                    }
                }
                PushObjectCommand(ifc);
                if (mi.Resolve().IsVirtual || mi.Resolve().IsAbstract)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldvirtftn, mi);
                }
                else
                    il.Emit(OpCodes.Ldftn, mi);
                il.Emit(OpCodes.Newobj, mb.ImportReference(value.constructor.constructor_info));
                return;
            }

            //ssyy
            if (!value.new_obj_awaited())
            {
                il.Emit(OpCodes.Ldarg_0);
            }
            //\ssyy
            is_dot_expr = false;
            
            EmitArguments(parameters, real_parameters);
            //ssyy изменил
            if (value.new_obj_awaited())
            {
                il.Emit(OpCodes.Newobj, mb.ImportReference(value.constructor.constructor_info));
            }
            else
            {
                il.Emit(OpCodes.Call, mb.ImportReference(value.constructor.constructor_info));
            }
            EmitFreePinnedVariables();
            if (tmp_dot == true)
            {
                //MethodInfo mi = value.compiled_method.method_info;
                if (value.constructor.constructor_info.DeclaringType.IsValueType && !NETGeneratorTools.IsPointer(mb.ImportReference(value.constructor.constructor_info).DeclaringType))
                {
                    Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mb.ImportReference(value.constructor.constructor_info).DeclaringType);
                    il.Body.Variables.Add(lb);
                    il.Emit(OpCodes.Stloc, lb);
                    il.Emit(OpCodes.Ldloca, lb);
                }
            }
            else
            {
                is_dot_expr = false;
            }
            //\ssyy  
            if (init_call_awaited && !value.new_obj_awaited())
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, cur_ti.init_meth);
                init_call_awaited = false;
            }
        }

        private bool TypeNeedToFix(ITypeNode type)
        {
            switch (type.type_special_kind)
            {
                case type_special_kind.array_wrapper:
                case type_special_kind.array_kind:
                case type_special_kind.set_type:
                case type_special_kind.short_string:
                case type_special_kind.typed_file:
                    return true;
            }
            return false;
        }

        //перевод @
        public override void visit(SemanticTree.IGetAddrNode value)
        {
            IExpressionNode to = value.addr_of_expr;
            if (to is INamespaceVariableReferenceNode)
            {
                AddrOfNamespaceVariableNode(to as INamespaceVariableReferenceNode);
            }
            else if (to is ILocalVariableReferenceNode || to is ILocalBlockVariableReferenceNode)
            {
                AddrOfLocalVariableNode(to as IReferenceNode);
            }
            else if (to is ICommonParameterReferenceNode)
            {
                AddrOfParameterNode((ICommonParameterReferenceNode)to);
            }
            else if (to is ICommonClassFieldReferenceNode)
            {
                AddrOfField((ICommonClassFieldReferenceNode)to);
            }
            else if (to is IStaticCommonClassFieldReferenceNode)
            {
                AddrOfStaticField((IStaticCommonClassFieldReferenceNode)to);
            }
            else if (to is ICompiledFieldReferenceNode)
            {
                AddrOfCompiledField((ICompiledFieldReferenceNode)to);
            }
            else if (to is IStaticCompiledFieldReferenceNode)
            {
                AddrOfStaticCompiledField((IStaticCompiledFieldReferenceNode)to);
            }
            else if (to is ISimpleArrayIndexingNode)
            {
                AddrOfArrayIndexing((ISimpleArrayIndexingNode)to);
            }
            else if (to is IDereferenceNode)
            {
                (to as IDereferenceNode).derefered_expr.visit(this);
                return;
            }
            else if (to is IThisNode)
            {
                bool tmp = is_dot_expr;
                is_dot_expr = true;
                (to as IThisNode).visit(this);
                is_dot_expr = tmp;
                return;
            }
            if (TypeNeedToFix(to.type))
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldind_Ref);
                FixPointer();
            }
            else
            {
                TypeInfo ti = helper.GetTypeReference(to.type);
                if (ti.fix_meth != null)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, ti.fix_meth);
                }
            }
        }

        private void AddrOfNamespaceVariableNode(INamespaceVariableReferenceNode value)
        {
            VarInfo vi = helper.GetVariable(value.variable);
            Mono.Cecil.FieldDefinition fb = vi.fb;
            il.Emit(OpCodes.Ldsflda, fb);
        }

        private void AddrOfLocalVariableNode(IReferenceNode value)
        {
            VarInfo vi = helper.GetVariable(value.Variable);
            if (vi.kind == VarKind.vkLocal)
            {
                Mono.Cecil.Cil.VariableDefinition lb = vi.lb;
                il.Emit(OpCodes.Ldloca, lb);
            }
            else if (vi.kind == VarKind.vkNonLocal)
            {
                Mono.Cecil.FieldDefinition fb = vi.fb;
                MethInfo cur_mi = smi.Peek();
                int dist = (smi.Peek()).num_scope - vi.meth.num_scope;
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                    cur_mi = cur_mi.up_meth;
                }
                il.Emit(OpCodes.Ldflda, fb);
            }
        }

        private void AddrOfParameterNode(ICommonParameterReferenceNode value)
        {
            ParamInfo pi = helper.GetParameter(value.parameter);
            if (pi.kind == ParamKind.pkNone)
            {
                Mono.Cecil.ParameterDefinition pb = pi.pb;
                //byte pos = (byte)(pb.Position-1);
                //***********************Kolay modified**********************
                byte pos = (byte)(pb.Index);
                if (is_constructor || cur_meth.IsStatic == false) pos = (byte)pb.Index;
                else pos = (byte)(pb.Index);
                //***********************End of Kolay modified**********************
                if (value.parameter.parameter_type != parameter_type.var)
                {
                    if (pos <= 255)
                        il.Emit(OpCodes.Ldarga_S, pos);
                    else
                        il.Emit(OpCodes.Ldarga, pos);
                }
                else
                {
                    if (pos <= 255)
                        il.Emit(OpCodes.Ldarg_S, pos);
                    else
                        il.Emit(OpCodes.Ldarg, pos);
                }
            }
            else
            {
                Mono.Cecil.FieldDefinition fb = pi.fb;
                MethInfo cur_mi = smi.Peek();
                int dist = (smi.Peek()).num_scope - pi.meth.num_scope;
                il.Emit(OpCodes.Ldloc, cur_mi.frame);
                for (int i = 0; i < dist; i++)
                {
                    il.Emit(OpCodes.Ldfld, cur_mi.disp.parent);
                    cur_mi = cur_mi.up_meth;
                }
                il.Emit(OpCodes.Ldflda, fb);
            }
        }

        private void AddrOfField(ICommonClassFieldReferenceNode value)
        {
            bool tmp_dot = is_dot_expr;
            if (tmp_dot == false)
                is_dot_expr = true;
            value.obj.visit(this);
            Mono.Cecil.FieldReference fi = helper.GetField(value.field).fi;
            il.Emit(OpCodes.Ldflda, fi);
            if (tmp_dot == false)
            {
                is_dot_expr = false;
            }
        }

        private void AddrOfStaticField(IStaticCommonClassFieldReferenceNode value)
        {
            bool tmp_dot = is_dot_expr;
            Mono.Cecil.FieldReference fi = helper.GetField(value.static_field).fi;
            il.Emit(OpCodes.Ldsflda, fi);
            if (tmp_dot == false)
            {
                is_dot_expr = false;
            }
        }

        private void AddrOfCompiledField(ICompiledFieldReferenceNode value)
        {
            if (mb.ImportReference(value.field.compiled_field).Resolve().IsLiteral == false)
            {
                value.obj.visit(this);
                il.Emit(OpCodes.Ldflda, mb.ImportReference(value.field.compiled_field));
            }
        }

        private void AddrOfStaticCompiledField(IStaticCompiledFieldReferenceNode value)
        {
            if (mb.ImportReference(value.static_field.compiled_field).Resolve().IsLiteral == false)
            {
                il.Emit(OpCodes.Ldsflda, mb.ImportReference(value.static_field.compiled_field));
            }
        }

        private void AddrOfArrayIndexing(ISimpleArrayIndexingNode value)
        {
            bool temp_is_addr = is_addr;
            bool temp_is_dot_expr = is_dot_expr;
            is_addr = false;
            is_dot_expr = false;
            var indices = value.indices;
            TypeInfo ti = helper.GetTypeReference(value.array.type);
            value.array.visit(this);
            il.Emit(OpCodes.Dup);
            FixPointer();
            //посещаем индекс
            //value.index.visit(this);
            ISimpleArrayNode arr_type = value.array.type as ISimpleArrayNode;
            TypeInfo elem_ti = null;
            Mono.Cecil.TypeReference elem_type = null;
            if (arr_type != null)
            {
                elem_ti = helper.GetTypeReference(arr_type.element_type);
                elem_type = elem_ti.tp;
            }
            else
                elem_type = ti.tp.GetElementType();
            Mono.Cecil.MethodReference addr_meth = null;
            if (indices == null)
            {
                value.index.visit(this);
            }
            else
            {
                List<Mono.Cecil.TypeReference> lst = new List<Mono.Cecil.TypeReference>();
                for (int i = 0; i < indices.Length; i++)
                    lst.Add(mb.TypeSystem.Int32);
                addr_meth = new Mono.Cecil.MethodReference("Address", elem_type.MakeByReferenceType(), ti.tp);
                addr_meth.HasThis = true;
                foreach (var paramType in lst)
                    addr_meth.Parameters.Add(new Mono.Cecil.ParameterDefinition(paramType));

                for (int i = 0; i < indices.Length; i++)
                    indices[i].visit(this);
            }

            if (value.indices == null)
                il.Emit(OpCodes.Ldelema, elem_type);
            else
                il.Emit(OpCodes.Call, addr_meth);
            is_addr = temp_is_addr;
            is_dot_expr = temp_is_dot_expr;
        }

        public override void visit(SemanticTree.IDereferenceNode value)
        {
            bool tmp = false;
            if (is_addr == true)
            {
                is_addr = false;
                tmp = true;
            }
            bool tmp_is_dot_expr = is_dot_expr;
            is_dot_expr = false;
            value.derefered_expr.visit(this);
            is_dot_expr = tmp_is_dot_expr;
            if (tmp == true) is_addr = true;
            TypeInfo ti = helper.GetTypeReference(((IRefTypeNode)value.derefered_expr.type).pointed_type);

            if (is_addr == false)
            {
                if (is_dot_expr == true)
                {
                    if (TypeIsClass(ti.tp)) il.Emit(OpCodes.Ldind_Ref);
                    else
                        if (ti.tp.IsPointer)
                            il.Emit(OpCodes.Ldind_I);
                }
                else
                {
                    NETGeneratorTools.PushParameterDereference(il, ti.tp);
                }
            }
            has_dereferences = true;
        }

        //перевод конструкции raise
        public override void visit(IThrowNode value)
        {
            value.exception_expresion.visit(this);
            il.Emit(OpCodes.Throw);
        }

        //перевод конструкции null
        public override void visit(INullConstantNode value)
        {
            il.Emit(value.type is IRefTypeNode ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);
        }

        struct TmpForCase
        {
            public IStatementNode stmt;
            public IConstantNode cnst;
        }

        struct TmpForLabel
        {
            public Instruction[] labels;
            public int low_bound;
            private void Test() { }
        }

        //перевод конструкции case
        public override void visit(ISwitchNode value)
        {
            //if (save_debug_info)
            //    MarkSequencePoint(value.Location);

            Instruction default_case = il.Create(OpCodes.Nop);
            Instruction jump_def_label = il.Create(OpCodes.Nop);
            Instruction end_label;
            bool in_if = false;
            if (if_stack.Count == 0)
                end_label = il.Create(OpCodes.Nop);
            else
            {
                end_label = if_stack.Pop();
                in_if = true;
            }
            Dictionary<IConstantNode, Instruction> dict;
            TmpForLabel[] case_labels = GetCaseSelectors(value, jump_def_label, out dict);
            value.case_expression.visit(this);
            Mono.Cecil.Cil.VariableDefinition lb = null;
            //if (case_labels.Length > 1)
            {
                lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Int32);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Stloc, lb);
            }
            for (int i = 0; i < case_labels.Length; i++)
            {
                if (lb != null)
                {
                    il.Emit(OpCodes.Ldloc, lb);
                    //il.Emit(OpCodes.Ldc_I4, case_labels[i].low_bound);
                    NETGeneratorTools.LdcIntConst(il, case_labels[i].low_bound);
                    il.Emit(OpCodes.Sub);
                }
                else
                {
                    //il.Emit(OpCodes.Ldc_I4, case_labels[i].low_bound);
                    NETGeneratorTools.LdcIntConst(il, case_labels[i].low_bound);
                    il.Emit(OpCodes.Sub);
                }
                il.Emit(OpCodes.Switch, case_labels[i].labels);
            }
            il.Append(jump_def_label);
            ConvertRangedSelectors(value, end_label, lb);
            il.Emit(OpCodes.Br, default_case);

            foreach (ICaseVariantNode cvn in value.case_variants)
                ConvertCaseVariantNode(cvn, end_label, dict);
            CompleteRangedSelectors(value, end_label);
            il.Append(default_case);

            if (value.default_statement != null)
            {
                if (value.default_statement.Location != null)
                    MarkSequencePoint(il, value.default_statement.Location.begin_line_num, value.default_statement.Location.begin_column_num, value.default_statement.Location.begin_line_num, value.default_statement.Location.begin_column_num);
                value.default_statement.visit(this);
            }
            //MarkSequencePoint(il,0xFeeFee,0xFeeFee,0xFeeFee,0xFeeFee);
            if (!in_if)
                il.Append(end_label);
        }

        public override void visit(IStatementsExpressionNode value)
        {
            foreach (IStatementNode statement in value.statements)
            {
                ConvertStatement(statement);
            }
            value.expresion.visit(this);
        }

        public override void visit(IQuestionColonExpressionNode value)
        {
            Instruction EndLabel = il.Create(OpCodes.Nop);
            Instruction FalseLabel = il.Create(OpCodes.Nop);
            bool tmp_is_dot_expr = is_dot_expr;
            bool tmp_is_addr = is_addr;
            is_dot_expr = false;//don't box the condition expression
            is_addr = false;
            Mono.Cecil.Cil.VariableDefinition funcptr_lb = null;
            if (value.condition is IBasicFunctionCallNode && 
                (value.condition as IBasicFunctionCallNode).real_parameters[0].type.IsDelegate &&
                (value.condition as IBasicFunctionCallNode).real_parameters[1] is INullConstantNode &&
                (value.condition as IBasicFunctionCallNode).basic_function.basic_function_type == basic_function_type.objeq)
            {
                IBasicFunctionCallNode eq = (value.condition as IBasicFunctionCallNode);
                funcptr_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference((value.condition as IBasicFunctionCallNode).real_parameters[0].type).tp);
                il.Body.Variables.Add(funcptr_lb);
                eq.real_parameters[0].visit(this);
                il.Emit(OpCodes.Stloc, funcptr_lb);
                il.Emit(OpCodes.Ldloc, funcptr_lb);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                if (value.ret_if_false is ICommonConstructorCall)
                    helper.LinkExpressionToLocalBuilder((value.condition as IBasicFunctionCallNode).real_parameters[0], funcptr_lb);
                else if (value.ret_if_false is ICompiledConstructorCall)
                    helper.LinkExpressionToLocalBuilder((value.condition as IBasicFunctionCallNode).real_parameters[0], funcptr_lb);
            }
            else
                value.condition.visit(this);

            is_dot_expr = tmp_is_dot_expr;
            is_addr = tmp_is_addr;
            il.Emit(OpCodes.Brfalse, FalseLabel);
            if (value.ret_if_true is INullConstantNode && value.ret_if_true.type.is_nullable_type)
            {
                Mono.Cecil.TypeReference tp = helper.GetTypeReference(value.ret_if_true.type).tp;
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Ldloca, lb);
                il.Emit(OpCodes.Initobj, tp);
                il.Emit(OpCodes.Ldloc, lb);
            }
            else
                value.ret_if_true.visit(this);
            var ti = helper.GetTypeReference(value.ret_if_true.type);
            if (ti != null)
                EmitBox(value.ret_if_true, ti.tp);
            il.Emit(OpCodes.Br, EndLabel);
            il.Append(FalseLabel);
            is_dot_expr = tmp_is_dot_expr;
            is_addr = tmp_is_addr;
            if (value.ret_if_false is INullConstantNode && value.ret_if_false.type.is_nullable_type)
            {
                Mono.Cecil.TypeReference tp = helper.GetTypeReference(value.ret_if_false.type).tp;
                Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(tp);
                il.Body.Variables.Add(lb);
                il.Emit(OpCodes.Ldloca, lb);
                il.Emit(OpCodes.Initobj, tp);
                il.Emit(OpCodes.Ldloc, lb);
            }
            else
                value.ret_if_false.visit(this);
            ti = helper.GetTypeReference(value.ret_if_false.type);
            if (ti != null)
                EmitBox(value.ret_if_false, ti.tp);
            
            il.Append(EndLabel);
            
        }

        public override void visit(IDoubleQuestionColonExpressionNode value)
        {
            Instruction EndLabel = il.Create(OpCodes.Nop);
            Instruction NullLabel = il.Create(OpCodes.Nop);
            bool tmp_is_dot_expr = is_dot_expr;
            bool tmp_is_addr = is_addr;
            is_dot_expr = false;//don't box the condition expression
            is_addr = false;
            Mono.Cecil.Cil.VariableDefinition tmp_lb = null;
            if (value.condition is IBasicFunctionCallNode &&
                (value.condition as IBasicFunctionCallNode).real_parameters[0].type.IsDelegate &&
                (value.condition as IBasicFunctionCallNode).real_parameters[1] is INullConstantNode &&
                (value.condition as IBasicFunctionCallNode).basic_function.basic_function_type == basic_function_type.objeq)
            {
                IBasicFunctionCallNode eq = (value.condition as IBasicFunctionCallNode);
                tmp_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference((value.condition as IBasicFunctionCallNode).real_parameters[0].type).tp);
                il.Body.Variables.Add(tmp_lb);
                eq.real_parameters[0].visit(this);
                il.Emit(OpCodes.Stloc, tmp_lb);
                il.Emit(OpCodes.Ldloc, tmp_lb);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                
            }
            else
            {
                value.condition.visit(this);
                if (value.condition.type.is_nullable_type)
                {
                    
                    tmp_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.type).tp);
                    il.Body.Variables.Add(tmp_lb);
                    il.Emit(OpCodes.Stloc, tmp_lb);
                    il.Emit(OpCodes.Ldloca, tmp_lb);
                    Mono.Cecil.MethodReference mi = null;
                    TypeInfo cond_ti = helper.GetTypeReference(value.condition.type);
                    mi = mb.ImportReference(TypeFactory.NullableHasValueGetMethod).AsMemberOf((Mono.Cecil.GenericInstanceType)cond_ti.tp);
                    il.Emit(OpCodes.Call, mi);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                }
                else
                {
                    tmp_lb = new Mono.Cecil.Cil.VariableDefinition(helper.GetTypeReference(value.type).tp);
                    il.Body.Variables.Add(tmp_lb);
                    il.Emit(OpCodes.Stloc, tmp_lb);
                    il.Emit(OpCodes.Ldloc, tmp_lb);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ceq);
                }
                
            }
                

            is_dot_expr = tmp_is_dot_expr;
            is_addr = tmp_is_addr;
            il.Emit(OpCodes.Brtrue, NullLabel);
            if (value.condition.type.is_nullable_type && tmp_is_dot_expr)
                il.Emit(OpCodes.Ldloca, tmp_lb);
            else
                il.Emit(OpCodes.Ldloc, tmp_lb);
            TypeInfo ti = helper.GetTypeReference(value.condition.type);
            if (ti != null)
                EmitBox(value.condition, ti.tp);
            il.Emit(OpCodes.Br, EndLabel);
            il.Append(NullLabel);
            value.ret_if_null.visit(this);
            if (ti != null)
                EmitBox(value.ret_if_null, ti.tp);
            il.Append(EndLabel);

        }

        private Hashtable range_stmts_labels = new Hashtable();

        //перевод селекторов-диапазонов case
        private void ConvertRangedSelectors(ISwitchNode value, Instruction end_label, Mono.Cecil.Cil.VariableDefinition lb)
        {
            foreach (ICaseVariantNode cvn in value.case_variants)
            {
                var ranges = cvn.ranges;
                if (ranges.Length > 0)
                {
                    Instruction range_stmts_label = il.Create(OpCodes.Nop);
                    for (int i = 0; i < ranges.Length; i++)
                    {
                        Instruction false_label = il.Create(OpCodes.Nop);
                        il.Emit(OpCodes.Ldloc, lb);
                        ranges[i].lower_bound.visit(this);
                        il.Emit(OpCodes.Blt, false_label);
                        il.Emit(OpCodes.Ldloc, lb);
                        ranges[i].high_bound.visit(this);
                        il.Emit(OpCodes.Bgt, false_label);
                        il.Emit(OpCodes.Br, range_stmts_label);
                        range_stmts_labels[cvn.statement_to_execute] = range_stmts_label;
                        il.Append(false_label);
                    }
                }
            }
        }

        private void CompleteRangedSelectors(ISwitchNode value, Instruction end_label)
        {
            foreach (ICaseVariantNode cvn in value.case_variants)
            {
                if (cvn.ranges.Length > 0)
                {
                    il.Append((Instruction)range_stmts_labels[cvn.statement_to_execute]);
                    if (save_debug_info)
                        MarkSequencePoint(cvn.statement_to_execute.Location);
                    ConvertStatement(cvn.statement_to_execute);
                    il.Emit(OpCodes.Br, end_label);
                }
            }
        }

        //перевод селекторов case
        public void ConvertCaseVariantNode(ICaseVariantNode value, Instruction end_label, Dictionary<IConstantNode, Instruction> dict)
        {
            if (save_debug_info)
            {
            	if (value.statement_to_execute.Location != null)
                	MarkSequencePoint(value.statement_to_execute.Location);
            	else
            		MarkSequencePoint(value.Location);
            }
            for (int i = 0; i < value.elements.Length; i++)
                il.Append(dict[value.elements[i]]);
            ConvertStatement(value.statement_to_execute);
            il.Emit(OpCodes.Br, end_label);
        }

        //сбор информации о селекторах (сортировка, группировка и т. д.)
        private TmpForLabel[] GetCaseSelectors(ISwitchNode value, Instruction default_label, out Dictionary<IConstantNode, Instruction> dict)
        {
            SortedDictionary<int, TmpForCase> sel_list = new System.Collections.Generic.SortedDictionary<int, TmpForCase>();
            Dictionary<ICaseRangeNode, IStatementNode> sel_range = new System.Collections.Generic.Dictionary<ICaseRangeNode, IStatementNode>();
            dict = new Dictionary<IConstantNode, Instruction>();
            //sobiraem informaciju o konstantah v case
            for (int i = 0; i < value.case_variants.Length; i++)
            {
                ICaseVariantNode cvn = value.case_variants[i];
                for (int j = 0; j < cvn.elements.Length; j++)
                {
                    IConstantNode cnst = cvn.elements[j];
                    if (cnst is IIntConstantNode)
                    {
                        TmpForCase tfc = new TmpForCase();
                        tfc.cnst = cnst;
                        tfc.stmt = cvn.statement_to_execute;
                        sel_list[((IIntConstantNode)cnst).constant_value] = tfc;
                    }
                    else if (cnst is ICharConstantNode)
                    {
                        TmpForCase tfc = new TmpForCase();
                        tfc.cnst = cnst;
                        tfc.stmt = cvn.statement_to_execute;
                        sel_list[(int)((ICharConstantNode)cnst).constant_value] = tfc;
                    }
                    else if (cnst is IBoolConstantNode)
                    {
                        TmpForCase tfc = new TmpForCase();
                        tfc.cnst = cnst;
                        tfc.stmt = cvn.statement_to_execute;
                        sel_list[Convert.ToInt32(((IBoolConstantNode)cnst).constant_value)] = tfc;
                    }
                }
            }
            System.Collections.Generic.List<int> lst = new System.Collections.Generic.List<int>();
            foreach (int val in sel_list.Keys)
            {
                lst.Add(val);
            }
            //sortiruem spisok perehodov v case
            lst.Sort();
            //int size = lst[lst.Count - 1] - lst[0] + 1;
            System.Collections.Generic.List<Instruction> label_list = new System.Collections.Generic.List<Instruction>();
            int last = 0;
            int size = 0;
            TmpForLabel tfl = new TmpForLabel();
            List<TmpForLabel> ltfl = new List<TmpForLabel>();
            //sozdaem metki dlja perehodov
            if (lst.Count > 0)
            {
                last = lst[0];
                size = 1;
                tfl.low_bound = last;//niznjaa granica
                Instruction l = il.Create(OpCodes.Nop);
                dict[sel_list[last].cnst] = l;//konstante sopostavim metku
                label_list.Add(l);
            }
            for (int i = 1; i < lst.Count; i++)
            {
                int dist = lst[i] - last;
                if (dist < 10)//esli rasstojanie mezhdu sosednimi konstantami nebolshoe
                {
                    last = lst[i];
                    size += dist;//pribavljaem rasstojanie
                    if (dist > 1)
                    {
                        for (int j = 1; j < dist; j++) //inache nado perehodit k proverke diapazonov
                            label_list.Add(default_label);
                    }
                    Instruction l = il.Create(OpCodes.Nop);
                    dict[sel_list[last].cnst] = l;
                    label_list.Add(l);
                }
                else
                {
                    tfl.labels = label_list.ToArray();//inache sozdaem otdelnuju tablicu perehodov
                    ltfl.Add(tfl);
                    tfl = new TmpForLabel();
                    label_list = new List<Instruction>();
                    tfl.low_bound = lst[i];
                    last = lst[i];
                    Instruction l = il.Create(OpCodes.Nop);
                    dict[sel_list[last].cnst] = l;
                    label_list.Add(l);
                }
            }
            tfl.labels = label_list.ToArray();
            ltfl.Add(tfl);
            return ltfl.ToArray();
        }

        public override void visit(SemanticTree.ICommonNestedInFunctionFunctionNode value)
        {

        }

        public override void visit(SemanticTree.ICommonNamespaceFunctionNode value)
        {

        }

        public override void visit(SemanticTree.ICommonFunctionNode value)
        {

        }

        public override void visit(SemanticTree.IBasicFunctionNode value)
        {

        }

        public override void visit(SemanticTree.INamespaceMemberNode value)
        {

        }

        public override void visit(SemanticTree.IFunctionMemberNode value)
        {

        }

        public override void visit(SemanticTree.ICommonClassMemberNode value)
        {

        }

        public override void visit(SemanticTree.ICompiledClassMemberNode value)
        {

        }

        public override void visit(SemanticTree.IClassMemberNode value)
        {

        }

        public override void visit(SemanticTree.IFunctionNode value)
        {

        }


        public override void visit(SemanticTree.IIsNode value)
        {
            bool idexpr = is_dot_expr;
            is_dot_expr = false;
            value.left.visit(this);
            if (!(value.left is INullConstantNode) && (value.left.type.is_value_type || value.left.type.is_generic_parameter))
                il.Emit(OpCodes.Box, helper.GetTypeReference(value.left.type).tp);
            is_dot_expr = idexpr;
            Mono.Cecil.TypeReference right = helper.GetTypeReference(value.right).tp;
            il.Emit(OpCodes.Isinst, right);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            if (is_dot_expr)
                NETGeneratorTools.CreateLocalAndLoad(il, mb.TypeSystem.Boolean);

        }

        public override void visit(SemanticTree.IAsNode value)
        {
            bool idexpr = is_dot_expr;
            is_dot_expr = false;
            value.left.visit(this);
            is_dot_expr = idexpr;
            Mono.Cecil.TypeReference right = helper.GetTypeReference(value.right).tp;
            if (!(value.left is SemanticTree.INullConstantNode) && (value.left.type.is_value_type || value.left.type.is_generic_parameter))
            {
                il.Emit(OpCodes.Box, helper.GetTypeReference(value.left.type).tp);
            }
            il.Emit(OpCodes.Isinst, right);
        }

        private void PushSize(Mono.Cecil.TypeReference t)
        {
            if (t.IsGenericParameter)
            {
                PushRuntimeSize(t);
                return;
            }
            if (t.HasGenericParameters)
            {
                PushRuntimeSize(t);
                return;
            }
            if (t.Resolve().IsEnum)
            {
                PushIntConst(Marshal.SizeOf(TypeFactory.Int32Type));
                return;
            }
            PushRuntimeSize(t);
            return;
        }


        private void PushRuntimeSize(Mono.Cecil.TypeReference t)
        {
            if (!t.IsValueType)
                t = mb.TypeSystem.IntPtr;
            if (t.FullName == mb.TypeSystem.IntPtr.FullName)
            {
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.EnvironmentIs64BitProcessGetMethod));
                Instruction brf = il.Create(OpCodes.Nop);
                Instruction bre = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Brfalse_S, brf);
                il.Emit(OpCodes.Ldc_I4_8);
                il.Emit(OpCodes.Br_S, bre);
                il.Append(brf);
                il.Emit(OpCodes.Ldc_I4_4);
                il.Append(bre);
            }
            else
            {
                //il.Emit(OpCodes.Ldtoken, t);
                NETGeneratorTools.PushTypeOf(il, t);
                il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.MarshalSizeOfMethod));
            }
            return;
        }

        private void PushSizeForSizeof(Mono.Cecil.TypeReference t, ITypeNode tn)
        {
            PushSize(t);
        }

        public override void visit(ISizeOfOperator value)
        {
            //void.System.Runtime.InteropServices.Marshal.SizeOf()
            Mono.Cecil.TypeReference tp = helper.GetTypeReference(value.oftype).tp;
            if (tp.IsPrimitive && tp.FullName != "System.IntPtr" && tp.FullName != "System.UIntPtr")
            {
                PushIntConst(TypeFactory.GetPrimitiveTypeSize(tp));
                return;
            }
            PushSizeForSizeof(tp, value.oftype);
        }

        public override void visit(ITypeOfOperator value)
        {
            NETGeneratorTools.PushTypeOf(il, helper.GetTypeReference(value.oftype).tp);
        }

        public override void visit(IExitProcedure value)
        {
            if (!ExitProcedureCall)
                ExitLabel = il.Create(OpCodes.Nop);
            if (!safe_block)
                il.Emit(OpCodes.Br, ExitLabel);
            else
                il.Emit(OpCodes.Leave, ExitLabel);
            ExitProcedureCall = true;
        }

        public override void visit(IArrayConstantNode value)
        {
            il.Emit(OpCodes.Ldsfld, GetConvertedConstants(value));
        }

        public override void visit(IRecordConstantNode value)
        {
            if (is_dot_expr)
                il.Emit(OpCodes.Ldsflda, GetConvertedConstants(value));
            else
                il.Emit(OpCodes.Ldsfld, GetConvertedConstants(value));
        }

        public override void visit(IEnumConstNode value)
        {
            PushIntConst(value.constant_value);
        }

        public override void visit(IClassConstantDefinitionNode value)
        {
            Mono.Cecil.FieldDefinition fb = null;
            if (value.type is ICompiledTypeNode && mb.ImportReference((value.type as ICompiledTypeNode).compiled_type).Resolve().IsEnum)
            {
                fb = new Mono.Cecil.FieldDefinition(value.name, FieldAttributes.Literal | ConvertFALToFieldAttributes(value.field_access_level), mb.TypeSystem.Int32);
                cur_type.Fields.Add(fb);
            }
            else if (value.constant_value.value != null)
            {
                fb = new Mono.Cecil.FieldDefinition(value.name, FieldAttributes.Literal | FieldAttributes.Static | ConvertFALToFieldAttributes(value.field_access_level), helper.GetTypeReference(value.type).tp);
                cur_type.Fields.Add(fb);
            }
            else
            {
                fb = new Mono.Cecil.FieldDefinition(value.name, FieldAttributes.Static | ConvertFALToFieldAttributes(value.field_access_level), helper.GetTypeReference(value.type).tp);
                cur_type.Fields.Add(fb);
            }
            if (value.constant_value.value != null)
                fb.Constant = value.constant_value.value;
            else
                helper.AddConstant(value, fb);
            //else
            //    throw new Errors.CompilerInternalError("NetGenerator", new Exception("Invalid constant value in IClassConstantDefinitionNode"));
        }

        public override void visit(ICompiledStaticMethodCallNodeAsConstant value)
        {
            value.MethodCall.visit(this);
        }

        public override void visit(ICommonStaticMethodCallNodeAsConstant value)
        {
            value.MethodCall.visit(this);
        }

        public override void visit(ICompiledConstructorCallAsConstant value)
        {
            value.MethodCall.visit(this);
        }

        public override void visit(ICommonNamespaceFunctionCallNodeAsConstant value)
        {
            value.MethodCall.visit(this);
        }

        public override void visit(IBasicFunctionCallNodeAsConstant value)
        {
            value.MethodCall.visit(this);
        }

        public override void visit(ICompiledStaticFieldReferenceNodeAsConstant value)
        {
            value.FieldReference.visit(this);
        }

        private void emit_numeric_conversion(Mono.Cecil.TypeReference to, Mono.Cecil.TypeReference from)
        {
            if (to != from)
            {
                if (helper.IsNumericType(to) && helper.IsNumericType(from))
                {
                    switch(to.MetadataType)
                    {
                        case Mono.Cecil.MetadataType.Byte: il.Emit(OpCodes.Conv_U1); break;
                        case Mono.Cecil.MetadataType.SByte: il.Emit(OpCodes.Conv_I1); break;
                        case Mono.Cecil.MetadataType.Int16: il.Emit(OpCodes.Conv_I2); break;
                        case Mono.Cecil.MetadataType.UInt16: il.Emit(OpCodes.Conv_U2); break;
                        case Mono.Cecil.MetadataType.Int32: il.Emit(OpCodes.Conv_I4); break;
                        case Mono.Cecil.MetadataType.UInt32: il.Emit(OpCodes.Conv_U4); break;
                        case Mono.Cecil.MetadataType.Int64: il.Emit(OpCodes.Conv_I8); break;
                        case Mono.Cecil.MetadataType.UInt64: il.Emit(OpCodes.Conv_U8); break;
                        case Mono.Cecil.MetadataType.Double: il.Emit(OpCodes.Conv_R8); break;
                        case Mono.Cecil.MetadataType.Single: il.Emit(OpCodes.Conv_R4); break;
                    }
                }
            }
        }
        private void emit_conversion(Mono.Cecil.TypeReference t)
        {
            switch (t.MetadataType)
            {
                case Mono.Cecil.MetadataType.Byte: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToByteMethod)); break;
                case Mono.Cecil.MetadataType.SByte: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToSByteMethod)); break;
                case Mono.Cecil.MetadataType.Int16: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToInt16Method)); break;
                case Mono.Cecil.MetadataType.UInt16: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToUInt16Method)); break;
                case Mono.Cecil.MetadataType.Int32: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToInt32Method)); break;
                case Mono.Cecil.MetadataType.UInt32: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToUInt32Method)); break;
                case Mono.Cecil.MetadataType.Int64: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToInt64Method)); break;
                case Mono.Cecil.MetadataType.UInt64: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToUInt64Method)); break;
                case Mono.Cecil.MetadataType.Char: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToCharMethod)); break;
                case Mono.Cecil.MetadataType.Boolean: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToBooleanMethod)); break;
                case Mono.Cecil.MetadataType.Double: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToDoubleMethod)); break;
                case Mono.Cecil.MetadataType.Single: il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.ConvertToSingleMethod)); break;
                default: il.Emit(OpCodes.Unbox_Any, t); break;
            }
        }

        public override void visit(IForeachNode value)
        {
            VarInfo vi = helper.GetVariable(value.VarIdent);
            Mono.Cecil.TypeReference in_what_type = helper.GetTypeReference(value.InWhatExpr.type).tp;

            Mono.Cecil.TypeReference return_type;
            Mono.Cecil.MethodReference enumer_mi;

            Mono.Cecil.TypeReference elementType = helper.GetTypeReference(value.ElementType).tp;

            bool is_generic = value.IsGeneric;

            if (is_generic)
            {
                var genericMethod = mb.ImportReference(TypeFactory.IEnumerableGenericGetEnumeratorMethod);

                var instancedInterf = mb.ImportReference(TypeFactory.IEnumerableGenericType).MakeGenericInstanceType(elementType);

                enumer_mi = genericMethod.AsMemberOf(instancedInterf);

                return_type = enumer_mi.ReturnType.GetElementType().MakeGenericInstanceType(elementType);
            }
            else
            {
                enumer_mi = mb.ImportReference(TypeFactory.IEnumerableGetEnumeratorMethod);
                return_type = enumer_mi.ReturnType;
            }

            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(return_type);
            il.Body.Variables.Add(lb);
            if (save_debug_info) il.Body.Method.DebugInformation.Scope.Variables.Add( new Mono.Cecil.Cil.VariableDebugInformation(lb, "$enumer$" + uid++) );

            value.InWhatExpr.visit(this);
            if (value.InWhatExpr.type.is_value_type)
                il.Emit(OpCodes.Box, in_what_type);
            il.Emit(OpCodes.Callvirt, enumer_mi);
            il.Emit(OpCodes.Stloc, lb);
            Instruction exl = il.BeginExceptionBlock();
            Instruction l1 = il.Create(OpCodes.Nop);
            Instruction l2 = il.Create(OpCodes.Nop);
            Instruction leave_label = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, l2);
            il.Append(l1);
            if (vi.kind == VarKind.vkNonLocal)
                il.Emit(OpCodes.Ldloc, (smi.Peek() as MethInfo).frame);
            if (lb.VariableType.IsValueType)
                il.Emit(OpCodes.Ldloca, lb);
            else 
                il.Emit(OpCodes.Ldloc, lb);
            Mono.Cecil.MethodReference get_current_meth = enumer_mi.ReturnType.Resolve()
                .GetMethods()
                .Single(item => item.Name == "get_Current");
            get_current_meth = mb.ImportReference(get_current_meth);

            if (enumer_mi.ReturnType.IsGenericInstance)
            {
                get_current_meth = get_current_meth.AsMemberOf( (Mono.Cecil.GenericInstanceType)return_type );
            }
            il.Emit(OpCodes.Callvirt, get_current_meth);
            if (vi.kind == VarKind.vkLocal)
            {
                if (!lb.VariableType.IsValueType && (vi.lb.VariableType.IsValueType || vi.lb.VariableType.IsGenericParameter))
                {
                    if (!is_generic)
                        emit_conversion(vi.lb.VariableType);
                    else
                        emit_numeric_conversion(vi.lb.VariableType, get_current_meth.ReturnType);
                }
                if (vi.lb.VariableType.FullName == mb.TypeSystem.Object.FullName && get_current_meth.ReturnType.IsValueType)
                    il.Emit(OpCodes.Box, get_current_meth.ReturnType);
                else if (vi.lb.VariableType.FullName == mb.TypeSystem.String.FullName && get_current_meth.ReturnType.FullName == mb.TypeSystem.Char.FullName)
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.CharToString));
                il.Emit(OpCodes.Stloc, vi.lb);
            }
            else if (vi.kind == VarKind.vkGlobal)
            {
                if (!lb.VariableType.IsValueType && (vi.fb.FieldType.IsValueType || vi.fb.FieldType.IsGenericParameter))
                {
                    if (!is_generic)
                        emit_conversion(vi.fb.FieldType);
                    else
                        emit_numeric_conversion(vi.fb.FieldType, get_current_meth.ReturnType);
                }
                if (vi.fb.FieldType.FullName == mb.TypeSystem.Object.FullName && get_current_meth.ReturnType.IsValueType)
                    il.Emit(OpCodes.Box, get_current_meth.ReturnType);
                else if (vi.fb.FieldType.FullName == mb.TypeSystem.String.FullName && get_current_meth.ReturnType.FullName == mb.TypeSystem.Char.FullName)
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.CharToString));
                il.Emit(OpCodes.Stsfld, vi.fb);
            }
            else
            {
                if (!lb.VariableType.IsValueType && (vi.fb.FieldType.IsValueType || vi.fb.FieldType.IsGenericParameter))
                {
                    if (!is_generic)
                        emit_conversion(vi.fb.FieldType);
                    else
                        emit_numeric_conversion(vi.fb.FieldType, get_current_meth.ReturnType);
                }
                if (vi.fb.FieldType.FullName == mb.TypeSystem.Object.FullName && get_current_meth.ReturnType.IsValueType)
                    il.Emit(OpCodes.Box, get_current_meth.ReturnType);
                else if (vi.fb.FieldType.FullName == mb.TypeSystem.String.FullName && get_current_meth.ReturnType.FullName == mb.TypeSystem.Char.FullName)
                    il.Emit(OpCodes.Call, mb.ImportReference(TypeFactory.CharToString));
                il.Emit(OpCodes.Stfld, vi.fb);
            }
            labels.Push(leave_label);
            clabels.Push(l2);
            var safe_block = EnterSafeBlock();
            ConvertStatement(value.Body);
            LeaveSafeBlock(safe_block);
            //MarkSequencePoint(value.Location);
            if (doc != null)
                il.Body.Method.DebugInformation.SequencePoints.Add(
                    new Mono.Cecil.Cil.SequencePoint(il.Body.Instructions.Last(), doc)
                    {
                        StartLine = 0xFeeFee, StartColumn = 0xFeeFee,
                        EndLine = 0xFeeFee, EndColumn = 0xFeeFee
                    }
                );
            il.Append(l2);
            if (lb.VariableType.IsValueType)
                il.Emit(OpCodes.Ldloca, lb);
            else 
                il.Emit(OpCodes.Ldloc, lb);
            il.Emit(OpCodes.Callvirt, mb.ImportReference(TypeFactory.IEnumeratorMoveNextMethod));
            il.Emit(OpCodes.Brtrue, l1);
            //il.Emit(OpCodes.Leave, leave_label);
            il.BeginFinallyBlock();
            //il.MarkLabel(br_lbl);
            bool is_disposable = false;
            if (helper.IsPascalType(return_type))
            {
                if (enumer_mi.ReturnType.Resolve().Methods.Any(item => item.Name == "Dispose"))
                    is_disposable = true;
            }
            else if (lb.VariableType.Resolve().Interfaces.Any(item => item.InterfaceType.FullName == "System.IDisposable"))
                is_disposable = true;
            if (is_disposable)
            {
                if (lb.VariableType.IsValueType)
                    il.Emit(OpCodes.Ldloca, lb);
                else
                    il.Emit(OpCodes.Ldloc, lb);
                il.Emit(OpCodes.Callvirt, mb.ImportReference(TypeFactory.IDisposableDisposeMethod));
            }

            il.EndExceptionBlock();
            il.Append(leave_label);
            labels.Pop();
            clabels.Pop();
        }


        Mono.Cecil.MethodReference _monitor_enter = null;
        Mono.Cecil.MethodReference _monitor_exit = null;

        public override void visit(ILockStatement value)
        {
            if (_monitor_enter == null)
            {
                _monitor_enter = mb.ImportReference(TypeFactory.MonitorEnterMethod);
                _monitor_exit = mb.ImportReference(TypeFactory.MonitorExitMethod);
            }
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(mb.TypeSystem.Object);
            il.Body.Variables.Add(lb);
            value.LockObject.visit(this);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc, lb);
            il.Emit(OpCodes.Call, _monitor_enter);
            il.BeginExceptionBlock();
            bool safe_block = EnterSafeBlock();
            ConvertStatement(value.Body);
            LeaveSafeBlock(safe_block);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, lb);
            il.Emit(OpCodes.Call, _monitor_exit);
            il.EndExceptionBlock();
        }

        public override void visit(IRethrowStatement value)
        {
            il.Emit(OpCodes.Rethrow);
        }

        public override void visit(ILocalBlockVariableReferenceNode value)
        {
            VarInfo vi = helper.GetVariable(value.Variable);
            if (vi == null)
            {
                ConvertLocalVariable(value.Variable, false, 0, 0);
                vi = helper.GetVariable(value.Variable);
            }
            Mono.Cecil.Cil.VariableDefinition lb = vi.lb;
            if (is_addr == false)//если это факт. var-параметр
            {
                if (is_dot_expr == true) //если после перем. в выражении стоит точка
                {
                    if (lb.VariableType.IsValueType || value.type.is_generic_parameter)
                    {
                        if (is_field_reference && value.type.is_generic_parameter && value.type.base_type != null && value.type.base_type.is_class && value.type.base_type.base_type != null)
                            il.Emit(OpCodes.Ldloc, lb);
                        else
                            il.Emit(OpCodes.Ldloca, lb);//если перем. размерного типа кладем ее адрес
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, lb);
                    }
                }
                else il.Emit(OpCodes.Ldloc, lb);
            }
            else il.Emit(OpCodes.Ldloca, lb);//в этом случае перем. - фактический var-параметр процедуры
        }

        public override void visit(INamespaceConstantReference value)
        {
            ConstInfo ci = helper.GetConstant(value.Constant);
            Mono.Cecil.FieldDefinition fb = ci.fb;
            if (is_addr == false)//если это факт. var-параметр
            {
                if (is_dot_expr == true) //если после перем. в выражении стоит точка
                {
                    if (fb.FieldType.IsValueType == true)
                    {
                        il.Emit(OpCodes.Ldsflda, fb);//если перем. размерного типа кладем ее адрес
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldsfld, fb);
                    }
                }
                else il.Emit(OpCodes.Ldsfld, fb);
            }
            else il.Emit(OpCodes.Ldsflda, fb);
        }

        public override void visit(IFunctionConstantReference value)
        {
            ConstInfo ci = helper.GetConstant(value.Constant);
            Mono.Cecil.FieldDefinition fb = ci.fb;
            if (is_addr == false)//если это факт. var-параметр
            {
                if (is_dot_expr == true) //если после перем. в выражении стоит точка
                {
                    if (fb.FieldType.IsValueType == true)
                    {
                        il.Emit(OpCodes.Ldsflda, fb);//если перем. размерного типа кладем ее адрес
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldsfld, fb);
                    }
                }
                else il.Emit(OpCodes.Ldsfld, fb);
            }
            else il.Emit(OpCodes.Ldsflda, fb);
        }

        public override void visit(IDefaultOperatorNodeAsConstant value)
        {
            value.DefaultOperator.visit(this);
        }

        public override void visit(ISizeOfOperatorAsConstant value)
        {
            value.SizeOfOperator.visit(this);
        }

        public override void visit(ITypeOfOperatorAsConstant value)
        {
            value.TypeOfOperator.visit(this);
        }

        public override void visit(ICommonConstructorCallAsConstant value)
        {
            value.ConstructorCall.visit(this);
        }

        public override void visit(IArrayInitializer value)
        {
            TypeInfo ti = helper.GetTypeReference(value.type);
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
            il.Body.Variables.Add(lb);
            CreateArrayLocalVariable(il, lb, ti, value, value.type);
            il.Emit(OpCodes.Ldloc, lb);
        }

        public override void visit(IRecordInitializer value)
        {
            TypeInfo ti = helper.GetTypeReference(value.type);
            Mono.Cecil.Cil.VariableDefinition lb = new Mono.Cecil.Cil.VariableDefinition(ti.tp);
            il.Body.Variables.Add(lb);
            CreateRecordLocalVariable(il, lb, ti, value);
            il.Emit(OpCodes.Ldloc, lb);
        }

        public override void visit(ICommonEventNode value)
        {
            Mono.Cecil.EventDefinition evb = new Mono.Cecil.EventDefinition(value.Name, EventAttributes.None, helper.GetTypeReference(value.DelegateType).tp);
            cur_type.Events.Add(evb);
            if (value.AddMethod != null)
                evb.AddMethod = helper.GetMethodBuilder(value.AddMethod);
            if (value.RemoveMethod != null)
                evb.RemoveMethod = helper.GetMethodBuilder(value.RemoveMethod);
            if (value.RaiseMethod != null)
                evb.InvokeMethod = helper.GetMethodBuilder(value.RaiseMethod);
            helper.AddEvent(value, evb);
        }

        public override void visit(ICommonNamespaceEventNode value)
        {
            Mono.Cecil.EventDefinition evb = new Mono.Cecil.EventDefinition(value.Name, EventAttributes.None, helper.GetTypeReference(value.DelegateType).tp);
            cur_type.Events.Add(evb);
            if (value.AddFunction != null)
                evb.AddMethod = helper.GetMethodBuilder(value.AddFunction);
            if (value.RaiseFunction != null)
                evb.RemoveMethod = helper.GetMethodBuilder(value.RaiseFunction);
            if (value.RaiseFunction != null)
                evb.InvokeMethod = helper.GetMethodBuilder(value.RaiseFunction);
            helper.AddEvent(value, evb);
        }

        public override void visit(IDefaultOperatorNode value)
        {
            Mono.Cecil.TypeReference t = helper.GetTypeReference(value.type).tp;
            Mono.Cecil.Cil.VariableDefinition def_var = new Mono.Cecil.Cil.VariableDefinition(t);
            il.Body.Variables.Add(def_var);
            il.Emit(OpCodes.Ldloca, def_var);
            il.Emit(OpCodes.Initobj, t);
            il.Emit(OpCodes.Ldloc, def_var);
        }

        public override void visit(INonStaticEventReference value)
        {
            value.obj.visit(this);
            if (value.Event is ICommonEventNode)
                il.Emit(OpCodes.Ldfld, helper.GetField((value.Event as ICommonEventNode).Field).fi);
        }

        public override void visit(IStaticEventReference value)
        {
            if (value.Event is ICommonEventNode)
                il.Emit(OpCodes.Ldsfld, helper.GetField((value.Event as ICommonEventNode).Field).fi);
            else
                il.Emit(OpCodes.Ldsfld, helper.GetVariable((value.Event as ICommonNamespaceEventNode).Field).fb);
        }
        
        public override void visit(SemanticTree.ILambdaFunctionNode value)
        {
        }

        public override void visit(SemanticTree.ILambdaFunctionCallNode value)
        {
        }
    }
}

