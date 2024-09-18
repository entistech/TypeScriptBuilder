using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EntisCore.TypeScriptBuilder
{
    public class TypeScriptGenerator
    {
        readonly HashSet<Type>
            _defined = new HashSet<Type>();
        readonly Stack<Type>
            _toDefine = new Stack<Type>();
        readonly SortedDictionary<string, CodeTextBuilder>
            _builder = new SortedDictionary<string, CodeTextBuilder>();

        readonly TypeScriptGeneratorOptions _options;
        readonly HashSet<Type>
            _exclude;

        public TypeScriptGenerator(TypeScriptGeneratorOptions options = null)
        {
            _exclude = new HashSet<Type>();
            _options = options ?? new TypeScriptGeneratorOptions();
        }

        public TypeScriptGenerator ExcludeType(Type type)
        {
            _exclude.Add(type);
            return this;
        }

        public TypeScriptGenerator AddCSType(Type type)
        {
            if (_defined.Add(type))
                _toDefine.Push(type);

            return this;
        }

        static string WithoutGeneric(Type type)
        {
            return type.Name.Split('`')[0];
        }

        public string TypeName(Type type, bool forceClass = false)
        {
            var
                ti = type.GetTypeInfo();

            if (ti.IsGenericParameter)
                return type.Name;

            var
                map = type.GetTypeInfo().GetCustomAttribute<TSMap>(false);

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    if (ti.IsEnum)
                    {
                        AddCSType(type);

                        return map == null ? type.Name : map.Name;
                    }

                    return "number";
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return "number";
                case TypeCode.Boolean:
                    return "boolean";
                case TypeCode.String:
                    return "string";
                case TypeCode.DateTime:
                    return "Date";
                case TypeCode.Object:
                    if (type.IsArray)
                        return TypeName(type.GetElementType()) + "[]";

                    if (type == typeof(Guid))
                        return "string";
                    
                    if (type == typeof(object))
                        return "any";

                    if (ti.IsGenericType)
                    {
                        var
                            genericType = ti.GetGenericTypeDefinition();
                        var
                            generics = ti.GetGenericArguments();

                        if (genericType == typeof(Dictionary<,>))
                        {
                            if (generics[0] == typeof(int) || generics[0] == typeof(string))
                                return $"{{ [index: {TypeName(generics[0])}]: {TypeName(generics[1])} }}";

                            return "{}";
                        }

                        // any other enumerable
                        if (genericType.GetInterfaces().Any(e => e.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
                            return TypeName(generics[0]) + "[]";

                        AddCSType(genericType);

                        map = genericType.GetTypeInfo().GetCustomAttribute<TSMap>(false);

                        return $"{NamespacePrefix(genericType)}{NormalizeInterface(map == null ? WithoutGeneric(genericType) : map.Name, forceClass)}<{string.Join(", ", generics.Select(e => TypeName(e)))}>";
                    }

                    AddCSType(type);

                    return NamespacePrefix(type) + NormalizeInterface(map == null ? type.Name : map.Name, forceClass);
                default:
                    return $"any /* {type.FullName} */";
            }
        }

        string NamespacePrefix(Type type)
        {
            return type.Namespace == _namespace || _options.IgnoreNamespaces ? "" : (type.Namespace + '.');
        }

        string _namespace = "";
        CodeTextBuilder Builder
        {
            get { return _builder[_namespace]; }
        }
        void SetNamespace(Type type)
        {
            _namespace = type.Namespace;

            if (!_builder.ContainsKey(_namespace))
                _builder[_namespace] = new CodeTextBuilder();
        }

        void GenerateTypeDefinition(Type type)
        {
            var
                ti = type.GetTypeInfo();

            if (ti.GetCustomAttribute<TSExclude>() != null || ti.GetCustomAttribute<ObsoleteAttribute>() != null || _exclude.Contains(type))
                return;

            SetNamespace(type);

            CommentClass(type);

            if (ti.IsEnum)
            {
                Builder.AppendLine($"export enum {TypeName(type)}");
                Builder.OpenScope();

                foreach (var e in Enum.GetValues(type))
                    Builder.AppendLine($"{e} = {Convert.ToInt32(e)},");

                Builder.CloseScope();
                return;
            }

            bool
                forceClass = ti.GetCustomAttribute<TSClass>() != null,
                flat = ti.GetCustomAttribute<TSFlat>() != null;

            Builder.Append($"export {(forceClass ? "class" : "interface")} {TypeName(type, forceClass)}");

            var
                baseType = ti.BaseType;

            if (ti.IsClass && !flat && baseType != null && baseType != typeof(object))
                Builder.AppendLine($" extends {TypeName(baseType)}");

            Builder.OpenScope();

            var
                flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static;
            if (!flat)
                flags |= BindingFlags.DeclaredOnly;

            // fields
            GenerateFields(
                type,
                type.GetFields(flags),
                f => f.FieldType,
                f => f.IsInitOnly,
                f => f.IsStatic,
                forceClass);

            // properties
            GenerateFields(
                type,
                type.GetProperties(flags),
                f => f.PropertyType,
                f => false,
                f => f.GetGetMethod().IsStatic,
                forceClass
            );

            Builder.CloseScope();
        }

        void GenerateFields<T>(Type type, T[] fields, Func<T, Type> getType, Func<T, bool> getReadonly, Func<T, bool> getStatic, bool forceClass) where T : MemberInfo
        {
            foreach (var f in fields)
            {
                if (f.GetCustomAttribute<TSExclude>() == null && f.GetCustomAttribute<ObsoleteAttribute>() == null)   // only fields defined in that type
                {
                    var fieldType = getType(f);


                    CommentMember(fieldType, f);

                    var nullable = Nullable.GetUnderlyingType(fieldType);

                    if (nullable != null)
                        fieldType = getType(f).GetGenericArguments()[0];

                    var optional = f.GetCustomAttribute<TSOptional>() != null;
                    
                    // If not the attribute was used, check if the type is nullable
                    if (!optional)
                    {
                        // Needs to fetch the Type from the PropertyInfo or FieldInfo-objects, for some resone
                        // the fieldType-variable does not contain this info.
                        Type realType = null;
                        if (f is PropertyInfo pInfo)
                        {
                            realType = pInfo.PropertyType;
                        }
                        else if (f is FieldInfo fInfo)
                        {
                            realType = fInfo.FieldType;
                        }

                        if (realType != null)
                        {
                            if (realType.IsGenericType && realType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            {
                                optional = true;
                            }
                        }
                      
                    }
                    

                    if (getStatic(f))
                        Builder.Append("static ");

                    if (_options.EmitReadonly && getReadonly(f))
                        Builder.Append("readonly ");

                    Builder.Append(NormalizeField(f.Name));
                    Builder.Append(optional ? "?" : "");
                    Builder.Append(": ");
                    Builder.Append(f.GetCustomAttribute<TSAny>() == null ? TypeName(fieldType) : "any");

                    var
                        init = f.GetCustomAttribute<TSInitialize>();
                    if (forceClass && init != null)
                        Builder.Append($" = {GenerateBody(init, f)}");

                    Builder.AppendLine(";");
                }
            }
        }

        private string GenerateBody(TSInitialize attribute, MemberInfo info)
        {
            if (attribute.Body != null)
                return attribute.Body;
            if (info is FieldInfo)
            {
                FieldInfo field = info as FieldInfo;
                return JsonSerializer.Serialize(field.GetRawConstantValue());
            }
            else
            {
                PropertyInfo property = info as PropertyInfo;
                return JsonSerializer.Serialize(property.GetRawConstantValue());
            }
        }

        private void CommentClass(Type type)
        {
            var summery = "";
            
            if (_options.EmitDocumentation)
            {
                summery = type.GetSummary();
            }
            
            AppendComment(summery,type.ToString());
            
        }

        private void CommentMember<T>(Type type, T memberInfo) where T : MemberInfo
        {
            var methodSummary = "";
            if (_options.EmitDocumentation)
            {
                methodSummary = memberInfo.GetSummary();
            }
            
            AppendComment(methodSummary,type.ToString());
        }

        private void AppendComment(string commentText, string type)
        {
            if(_options.EmitComments && _options.EmitDocumentation)
            {
                Builder.AppendLine($"/** {commentText} ({type}) */");
            }
            else if (_options.EmitComments && !string.IsNullOrEmpty(type))
            {
                Builder.AppendLine($"/** {type} */");
            }
            else if (_options.EmitDocumentation && !string.IsNullOrEmpty(commentText))
            {
                Builder.AppendLine($"/** {commentText} */");
            }

        }
        
        
        public string NormalizeField(string name)
        {
            if (!_options.UseCamelCase)
                return name;

            return char.ToLower(name[0]) + name.Substring(1);
        }

        public string NormalizeInterface(string name, bool forceClass)
        {
            if (forceClass || !_options.EmitIinInterface)
                return name;

            return 'I' + name;
        }

        public override string ToString()
        {
            while (_toDefine.Count > 0)
                GenerateTypeDefinition(_toDefine.Pop());

            var builder = new CodeTextBuilder();

            builder.AppendLine("// NOTE: This file is auto-generated. Any changes will be overwritten.");

            foreach (var e in _builder)
            {
                if (!_options.IgnoreNamespaces)
                {
                    builder.AppendLine($"namespace {e.Key}");
                    builder.OpenScope();
                }

                builder.AppendLine(e.Value.ToString());
                
                if (!_options.IgnoreNamespaces)
                    builder.CloseScope();
            }

            return builder.ToString();
        }

        public void Store(string file)
        {
            File.WriteAllText(file, ToString());
        }
    }
}
