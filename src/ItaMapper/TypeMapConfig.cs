﻿using ItaMapper.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ItaMapper.Exceptions;

namespace ItaMapper
{
    public class TypeMapConfig<A, B>
    {
        private readonly HashSet<string> targets;

        public TypeMapConfig()
        {
            Actions = new List<MappingAction<A, B>>();
            targets = new HashSet<string>();
        }

        private TypeMapConfig(IEnumerable<MappingAction<A, B>> actions, IEnumerable<string> targets)
        {
            Actions = actions.ToList();
            this.targets = new HashSet<string>(targets);
        }

        public TypeMapConfig<A, B> AddAction(MappingAction<A, B> action)
        {
            return new TypeMapConfig<A, B>(Actions.Append(action), targets.AppendIfNotNull(action.Target));
        }

        public IEnumerable<MappingAction<A, B>> Actions { get; }

        public IEnumerable<string> Targets => targets;

        public bool Targeting(string target) => targets.Contains(target);
    }

    public static class TypeMapConfigExtensions
    {
        //should throw a better error for unmappable properties
        public static TypeMapConfig<A, B> MapRemainingProperties<A, B>(this TypeMapConfig<A, B> config)
        {
            return typeof(B).GetProperties()
                .Where(p => !config.Targeting(p.Name))
                .Aggregate(config, (c, p) => c.AddAction(new DirectPropertyMap<A, B>(p.Name)));
        }

        public static TypeMapConfig<A, B> AssertAllPropertiesAreMapped<A, B>(this TypeMapConfig<A, B> config)
        {
            typeof(B).GetProperties()
                .FirstOrDefault(p => !config.Targeting(p.Name))
                ?.Do(p => throw new UnmappedPropertyException<A, B>(p));

            return config;
        }

        //todo remove this
        public static TypeMapConfig<A, B> Map<A, B, C>(this TypeMapConfig<A, B> config, Expression<Func<B, object>> selector, Func<PropertyMapArguments<A, B>, C> map)
        {
            return config.AddAction(new InlinePropertyMap<A, B, C>(selector, map));
        }

        public static TypeMapConfig<A, B> Map<A, B>(this TypeMapConfig<A, B> config, Expression<Func<B, object>> selector, Func<PropertyMapArguments<A, B>, TypedObject> map)
        {
            return config.AddAction(new InlinePropertyMap<A, B>(selector, map));
        }

        public static TypeMapConfig<A, B> Ignore<A, B>(this TypeMapConfig<A, B> config, Expression<Func<B, object>> selector)
        {
            return config.AddAction(new NoopAction<A, B>(selector));
        }

        public static TypeMap ToMap<A, B>(this TypeMapConfig<A, B> config) => new ActionAggregateTypeMap<A, B>(config);

        public static TypeMapConfig<A, B> Before<A, B>(this TypeMapConfig<A, B> config, Action<A, B, MappingContext> before)
        {
            return config.AddAction(new TargetFreeAction<A, B>(before, MappingPhase.BeforeMapping));
        }

        public static TypeMapConfig<A, B> After<A, B>(this TypeMapConfig<A, B> config, Action<A, B, MappingContext> after)
        {
            return config.AddAction(new TargetFreeAction<A, B>(after, MappingPhase.AfterMapping));
        }

        public static TypeMapContext<A, B> Map<A, B>(this TypeMapConfig<A, B> config, Expression<Func<B, object>> selector)
        {
            return new TypeMapContext<A, B>(config, selector);
        }
    }

    public class UnmappedPropertyException<A, B> : Exception
    {
        public UnmappedPropertyException(PropertyInfo pi)
            : base($"TypeMap {typeof(A)} -> {typeof(B)} does not map to destination property '{pi.Name}'")
        {
        }
    }

    public class TypeMapContext<A, B>
    {
        private readonly TypeMapConfig<A, B> config;
        private readonly Expression<Func<B, object>> selector;

        public TypeMapContext(TypeMapConfig<A, B> config, Expression<Func<B, object>> selector)
        {
            this.config = config;
            this.selector = selector;
        }

        public TypeMapConfig<A, B> WithAction(Func<Expression<Func<B, object>>, MappingAction<A, B>> action)
        {
            return config.AddAction(action(selector));
        }

        public TypeMapConfig<A, B> Using(Func<PropertyMapArguments<A, B>, ValueResolver<A, B>> factory)
        {
            return config.AddAction(new ResolverPropertyMap<A, B>(selector, factory));
        }

        public TypeMapConfig<A, B> Using(ValueResolver<A, B> resolver)
        {
            return Using(_ => resolver);
        }

        public TypeMapConfig<A, B> Using(Type resolver)
        {
            if (!typeof(ValueResolver<A, B>).IsAssignableFrom(resolver))
                throw new TypeIsNotResolverException<A, B>(resolver);

            return Using(args => (ValueResolver<A, B>)args.Context.Instantiator.Create(resolver));
        }

        //todo can I pull this out to an extension?
        public TypeMapConfig<A, B> Using<C>() where C : ValueResolver<A, B> => Using(typeof(C));
    }

    public static class TypeMapContextExtensions
    {
        public static TypeMapConfig<A, B> Using<A, B, C>(this TypeMapContext<A, B> context, Func<PropertyMapArguments<A, B>, C> map)
        {
            return context.Using(new InlineResolver<A, B, C>(map));
        }

        public static TypeMapConfig<A, B> From<A, B, C>(this TypeMapContext<A, B> context, Func<A, C> map)
        {
            return context.Using(new InlineResolver<A, B, C>(args => args.Source.Pipe(map)));
        }

        public static TypeMapConfig<A, B> FromNamedProperty<A, B>(this TypeMapContext<A, B> context, string name)
        {
            return context.WithAction(_ => new DirectPropertyMap<A, B>(name));
        }

        public static TypeMapConfig<A, B> ToSelf<A, B>(this TypeMapContext<A, B> context)
        {
            return context.WithAction(s => new DirectPropertyMap<A, B>(s));
        }

        public static TypeMapConfig<A, B> Ignore<A, B>(this TypeMapContext<A, B> context)
        {
            return context.WithAction(e => new NoopAction<A, B>(e));
        }
    }

    public interface ValueResolver<A, B>
    {
        object Resolve(PropertyMapArguments<A, B> args);

        //todo pretend this is not a smell
        Type MemberType { get; }
    }

    public abstract class ValueResolver<A, B, C> : ValueResolver<A, B>
    {
        public object Resolve(PropertyMapArguments<A, B> args)
        {
            return ResolveValue(args);
        }

        protected abstract C ResolveValue(PropertyMapArguments<A, B> args);

        public Type MemberType { get; } = typeof(C);
    }

    public class InlineResolver<A, B, C> : ValueResolver<A, B, C>
    {
        private readonly Func<PropertyMapArguments<A, B>, C> map;

        public InlineResolver(Func<PropertyMapArguments<A, B>, C> map) => this.map = map;

        protected override C ResolveValue(PropertyMapArguments<A, B> args) => map(args);
    }
}