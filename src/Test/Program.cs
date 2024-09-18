﻿using System;
using System.Collections.Generic;
using TestA;
using EntisCore.TypeScriptBuilder;

namespace Test
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = new TypeScriptGenerator(new TypeScriptGeneratorOptions
            {
                EmitDocumentation = false,
                EmitComments = true
            });

            builder
                .ExcludeType(typeof(Program))
                .AddCSType(typeof(Poco));
                //.AddCSType(typeof(TestA.Employee));
                //.AddCSType(typeof(TestA.Equipment))
                //.AddCSType(typeof(TestB.Strange<>));

            builder.Store("Test.ts");
        }
    }
}

namespace TestA
{

    /// <summary>
    /// A Poco or Plain Old Csharp Object
    /// </summary>
    public class Poco
    {
        /// <summary>
        /// Unique key for the member
        /// </summary>
        public Guid Key { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        
        public DateTime? BirthDate { get; set; }
        
    }
    
    [TSMap("UserType")]
    public enum EmployeeType : short
    {
        Normal = 1,
        Temporary = 2
    }

    [TSMap("FunkyEntity")]
    public class Entity<T>
    {
        public T Id;

        public TestB.Pair<int, Entity<int>> Map1;
        public TestB.Pair<string, Entity<string>> Map2;

        [TSAny]
        public DateTimeOffset TestAny;
    }

    [TSFlat]
    public class Equipment: Entity<short>
    {
        [TSOptional]
        public int Code;
        public Dictionary<double, string> ObjectDictionary;
    }

    public class Employee : Entity<int>
    {
        public Guid Key;
        public string Login;
        public EmployeeType? EmployeeType;

        public string[] ArrayTest;
        public List<DateTime> ListTest;

        public Dictionary<int, DateTime> DictIntTest;
        public Dictionary<string, DateTime> DictStringTest;

        public ICollection<Entity<DateTime>> CollectionTest;

        public Stamp CreatedBy;

        // exclude
        [TSAny]
        public Skip Skip;
        [TSExclude]
        public int Skip2;
    }

    public struct Stamp
    {
        public short UserId;
        public string User;
    }

    [TSExclude]
    public class Skip
    {
    }
}

namespace TestB
{
    public class Pair<T1, T2>
    {
        public T1 t1;
        public T2 t2;

        public int TestProperty { get; set; }

        void Test([TSAny]int n)
        {
        }
    }

    [TSClass]
    public class Strange<T>
    {
        [TSInitialize("{}")]
        public readonly Dictionary<int, TestA.Entity<T>> Test;
    }
}