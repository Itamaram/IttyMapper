﻿using System;
using System.Diagnostics;
using NUnit.Framework;

namespace ItaMapper.Tests
{
    public class Foo
    {
        public string Value { get; set; }
    }

    public class Bar
    {
        public string Value { get; set; }
        public string Value2 { get; set; }
    }

    public class TypeMapTests
    {
        [Test]
        public void Sanity()
        {
            var config = new TypeMapConfig<Foo, Bar>().Ignore(b => b.Value2).MapRemainingProperties();
            var mapper = new ItaMapper(new[] { new ActionAggregateTypeMap<Foo, Bar>(config) });
            var bar = mapper.Map<Bar>(new Foo { Value = "optimism" });
            Assert.AreEqual("optimism", bar.Value);
        }

        [Test]
        public void FunkyExtension()
        {
            var config = new TypeMapConfig<Foo, Bar>()
                .Map(b => b.Value2, args => args.Source.Value);
            var mapper = new ItaMapper(new[] { new ActionAggregateTypeMap<Foo, Bar>(config) });
            var bar = mapper.Map<Bar>(new Foo { Value = "X" });

            Assert.Null(bar.Value);
            Assert.AreEqual("X", bar.Value2);
        }
    }

    public class ExpressionBuilderTests
    {
        [Test]
        public void SetterTest()
        {
            var setter = new ExpressionBuilder().Setter(typeof(Foo), nameof(Foo.Value));
            var foo = new Foo();
            setter.Invoke(foo, "powah!");
            Assert.AreEqual("powah!", foo.Value);
        }

        [Test]
        public void GetterTest()
        {
            var getter = new ExpressionBuilder().Getter(typeof(Foo), nameof(Foo.Value));
            var foo = new Foo { Value = "bazinga" };
            Assert.AreEqual("bazinga", getter(foo));
        }
    }

    public class SettersPerformanceTest
    {
        [Category("Performance")]
        [TestCase(typeof(ExpressionSetterFactory))]
        [TestCase(typeof(ReflectionSetterFactory))]
        public void CrankIt(Type factory)
        {
            var expr = (Activator.CreateInstance(factory) as SimpleSetterFactory).SetterFor<Foo>("Value");
            var foo = new Foo();
            expr.Invoke(foo, "hello!");
            Assert.AreEqual("hello!", foo.Value);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 1_000_000; i++)
                expr.Invoke(foo, "value");
            sw.Stop();

            Console.WriteLine($"1mil {factory.Name} iter: {sw.ElapsedMilliseconds:N0}ms");
        }
    }

    public static class Extensions
    {
        public static Action<object, object> SetterFor<A>(this SimpleSetterFactory factory, string member)
        {
            return factory.SetterFor(typeof(A), member);
        }
    }
}