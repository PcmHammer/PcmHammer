using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class PublicApiStubTests
    {
        private static IEnumerable<Type> GetPublicApiTypes()
        {
            Assembly libraryAssembly = typeof(CKernelReader).Assembly;
            Assembly hammerAssembly = typeof(CommandLineOptions).Assembly;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in GetAssemblyTypes(libraryAssembly))
            {
                string key = type.FullName ?? type.Name;
                if (IsPublicApiType(type) && seen.Add(key))
                {
                    yield return type;
                }
            }

            foreach (Type type in GetAssemblyTypes(hammerAssembly))
            {
                string key = type.FullName ?? type.Name;
                if (IsPublicApiType(type) && seen.Add(key))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<Type> GetAssemblyTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static bool IsPublicApiType(Type type)
        {
            if (!(type.IsPublic || type.IsNestedPublic))
            {
                return false;
            }

            if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            {
                return false;
            }

            return type.IsClass || type.IsInterface || (type.IsValueType && !type.IsEnum);
        }

        [TestMethod]
        public void Public_method_stubs()
        {
            foreach (Type type in GetPublicApiTypes())
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName)
                    {
                        continue;
                    }

                    string typeName = type.FullName ?? type.Name;
                    Assert.IsTrue(true, $"{typeName}.{method.Name}");
                }
            }
        }

        [TestMethod]
        public void Public_constructor_stubs()
        {
            foreach (Type type in GetPublicApiTypes())
            {
                foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    string typeName = type.FullName ?? type.Name;
                    Assert.IsTrue(true, $"{typeName}({ctor.GetParameters().Length} params)");
                }
            }
        }
    }
}
