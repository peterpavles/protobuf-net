﻿#if !NO_RUNTIME
using System;
using System.Reflection;

using ProtoBuf.Meta;

namespace ProtoBuf.Serializers
{
    internal sealed class NullDecorator : ProtoDecoratorBase
    {
        public const int Tag = 1;
        public NullDecorator(TypeModel model, IProtoSerializer tail) : base(tail)
        {
            if (!tail.ReturnsValue)
                throw new NotSupportedException("NullDecorator only supports implementations that return values");

            Type tailType = tail.ExpectedType;
            if (Helpers.IsValueType(tailType))
            {
                ExpectedType = model.MapType(typeof(Nullable<>)).MakeGenericType(tailType);
            }
            else
            {
                ExpectedType = tailType;
            }
        }

        public override Type ExpectedType { get; }

        public override bool ReturnsValue => true;

        public override bool RequiresOldValue => true;

#if FEAT_COMPILER
        protected override void EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            using (Compiler.Local oldValue = ctx.GetLocalWithValue(ExpectedType, valueFrom))
            using (Compiler.Local token = new Compiler.Local(ctx, ctx.MapType(typeof(SubItemToken))))
            using (Compiler.Local field = new Compiler.Local(ctx, ctx.MapType(typeof(int))))
            {
                ctx.LoadReader(true);
                ctx.EmitCall(ctx.MapType(typeof(ProtoReader)).GetMethod("StartSubItem",
                    ProtoReader.State.ReaderStateTypeArray));
                ctx.StoreValue(token);

                Compiler.CodeLabel next = ctx.DefineLabel(), processField = ctx.DefineLabel(), end = ctx.DefineLabel();

                ctx.MarkLabel(next);

                ctx.EmitBasicRead("ReadFieldHeader", ctx.MapType(typeof(int)));
                ctx.CopyValue();
                ctx.StoreValue(field);
                ctx.LoadValue(Tag); // = 1 - process
                ctx.BranchIfEqual(processField, true);
                ctx.LoadValue(field);
                ctx.LoadValue(1); // < 1 - exit
                ctx.BranchIfLess(end, false);

                // default: skip
                ctx.LoadReader(true);
                ctx.EmitCall(ctx.MapType(typeof(ProtoReader)).GetMethod("SkipField", ProtoReader.State.StateTypeArray));
                ctx.Branch(next, true);

                // process
                ctx.MarkLabel(processField);
                if (Tail.RequiresOldValue)
                {
                    if (Helpers.IsValueType(ExpectedType))
                    {
                        ctx.LoadAddress(oldValue, ExpectedType);
                        ctx.EmitCall(ExpectedType.GetMethod("GetValueOrDefault", Helpers.EmptyTypes));
                    }
                    else
                    {
                        ctx.LoadValue(oldValue);
                    }
                }
                Tail.EmitRead(ctx, null);
                // note we demanded always returns a value
                if (Helpers.IsValueType(ExpectedType))
                {
                    ctx.EmitCtor(ExpectedType, Tail.ExpectedType); // re-nullable<T> it
                }
                ctx.StoreValue(oldValue);
                ctx.Branch(next, false);

                // outro
                ctx.MarkLabel(end);

                ctx.LoadValue(token);
                ctx.LoadReader(true);
                ctx.EmitCall(ctx.MapType(typeof(ProtoReader)).GetMethod("EndSubItem",
                    new[] { typeof(SubItemToken), typeof(ProtoReader), ProtoReader.State.ByRefStateType }));
                ctx.LoadValue(oldValue); // load the old value
            }
        }
        protected override void EmitWrite(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            using (Compiler.Local valOrNull = ctx.GetLocalWithValue(ExpectedType, valueFrom))
            using (Compiler.Local token = new Compiler.Local(ctx, ctx.MapType(typeof(SubItemToken))))
            {
                ctx.LoadNullRef();
                ctx.LoadWriter();
                ctx.EmitCall(ctx.MapType(typeof(ProtoWriter)).GetMethod("StartSubItem"));
                ctx.StoreValue(token);

                if (Helpers.IsValueType(ExpectedType))
                {
                    ctx.LoadAddress(valOrNull, ExpectedType);
                    ctx.LoadValue(ExpectedType.GetProperty("HasValue"));
                }
                else
                {
                    ctx.LoadValue(valOrNull);
                }
                Compiler.CodeLabel @end = ctx.DefineLabel();
                ctx.BranchIfFalse(@end, false);
                if (Helpers.IsValueType(ExpectedType))
                {
                    ctx.LoadAddress(valOrNull, ExpectedType);
                    ctx.EmitCall(ExpectedType.GetMethod("GetValueOrDefault", Helpers.EmptyTypes));
                }
                else
                {
                    ctx.LoadValue(valOrNull);
                }
                Tail.EmitWrite(ctx, null);

                ctx.MarkLabel(@end);

                ctx.LoadValue(token);
                ctx.LoadWriter();
                ctx.EmitCall(ctx.MapType(typeof(ProtoWriter)).GetMethod("EndSubItem"));
            }
        }
#endif

        public override object Read(ProtoReader source, ref ProtoReader.State state, object value)
        {
            SubItemToken tok = ProtoReader.StartSubItem(source, ref state);
            int field;
            while ((field = source.ReadFieldHeader(ref state)) > 0)
            {
                if (field == Tag)
                {
                    value = Tail.Read(source, ref state, value);
                }
                else
                {
                    source.SkipField(ref state);
                }
            }
            ProtoReader.EndSubItem(tok, source, ref state);
            return value;
        }

        public override void Write(object value, ProtoWriter dest)
        {
            SubItemToken token = ProtoWriter.StartSubItem(null, dest);
            if (value != null)
            {
                Tail.Write(value, dest);
            }
            ProtoWriter.EndSubItem(token, dest);
        }
    }
}
#endif