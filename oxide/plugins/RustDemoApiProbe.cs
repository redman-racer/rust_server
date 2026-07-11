using System;
using System.Linq;
using System.Reflection;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Rust Demo API Probe", "OpenAI", "0.1.0")]
    [Description("Lists current Rust server methods and types related to demo recording without starting a recording.")]
    public class RustDemoApiProbe : RustPlugin
    {
        [ConsoleCommand("demoprobe.scan")]
        private void Scan(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && arg.Connection.authLevel < 2)
                return;

            Puts("=== Rust demo API probe started ===");
            DumpType(typeof(BasePlayer));

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }
                catch { continue; }

                foreach (var type in types.Where(IsRelevantType).OrderBy(x => x.FullName))
                    DumpType(type);
            }

            Puts("=== Rust demo API probe finished ===");
        }

        private bool IsRelevantType(Type type)
        {
            if (type == null || string.IsNullOrEmpty(type.FullName)) return false;
            string name = type.FullName.ToLowerInvariant();
            return name.Contains("demo") &&
                   (name.Contains("record") || name.Contains("server") || name.Contains("player"));
        }

        private void DumpType(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;

            var methods = type.GetMethods(flags)
                .Where(IsRelevantMethod)
                .OrderBy(x => x.Name)
                .ToArray();

            var fields = type.GetFields(flags)
                .Where(x => ContainsKeyword(x.Name) || ContainsKeyword(x.FieldType.FullName))
                .OrderBy(x => x.Name)
                .ToArray();

            var properties = type.GetProperties(flags)
                .Where(x => ContainsKeyword(x.Name) || ContainsKeyword(x.PropertyType.FullName))
                .OrderBy(x => x.Name)
                .ToArray();

            if (methods.Length == 0 && fields.Length == 0 && properties.Length == 0)
                return;

            Puts($"TYPE {type.Assembly.GetName().Name}: {type.FullName}");
            foreach (var method in methods)
            {
                string parameters = string.Join(", ", method.GetParameters()
                    .Select(x => $"{FriendlyName(x.ParameterType)} {x.Name}"));
                Puts($"  METHOD {FriendlyName(method.ReturnType)} {method.Name}({parameters}) [{Visibility(method)}]");
            }

            foreach (var field in fields)
                Puts($"  FIELD {FriendlyName(field.FieldType)} {field.Name} [{Visibility(field)}]");

            foreach (var property in properties)
                Puts($"  PROPERTY {FriendlyName(property.PropertyType)} {property.Name}");
        }

        private bool IsRelevantMethod(MethodInfo method)
        {
            if (ContainsKeyword(method.Name) || ContainsKeyword(method.ReturnType.FullName))
                return true;

            return method.GetParameters().Any(x => ContainsKeyword(x.ParameterType.FullName));
        }

        private bool ContainsKeyword(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            value = value.ToLowerInvariant();
            return value.Contains("demo") || value.Contains("record");
        }

        private string FriendlyName(Type type)
        {
            if (type == null) return "null";
            if (!type.IsGenericType) return type.FullName ?? type.Name;
            string name = type.GetGenericTypeDefinition().FullName;
            name = name.Substring(0, name.IndexOf('`'));
            return name + "<" + string.Join(",", type.GetGenericArguments().Select(FriendlyName)) + ">";
        }

        private string Visibility(MethodBase method)
        {
            if (method.IsPublic) return "public";
            if (method.IsFamily) return "protected";
            if (method.IsAssembly) return "internal";
            return "private";
        }

        private string Visibility(FieldInfo field)
        {
            if (field.IsPublic) return "public";
            if (field.IsFamily) return "protected";
            if (field.IsAssembly) return "internal";
            return "private";
        }
    }
}
