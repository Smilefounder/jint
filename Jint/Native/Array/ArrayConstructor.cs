﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Jint.Collections;
using Jint.Native.Function;
using Jint.Native.Iterator;
using Jint.Native.Object;
using Jint.Native.Proxy;
using Jint.Native.Symbol;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Descriptors.Specialized;
using Jint.Runtime.Interop;

namespace Jint.Native.Array
{
    public sealed class ArrayConstructor : FunctionInstance, IConstructor
    {
        private static readonly JsString _functionName = new JsString("Array");

        private ArrayConstructor(Engine engine) :  base(engine, _functionName, false)
        {
        }

        public ArrayPrototype PrototypeObject { get; private set; }

        public static ArrayConstructor CreateArrayConstructor(Engine engine)
        {
            var obj = new ArrayConstructor(engine)
            {
                _prototype = engine.Function.PrototypeObject
            };

            // The value of the [[Prototype]] internal property of the Array constructor is the Function prototype object
            obj.PrototypeObject = ArrayPrototype.CreatePrototypeObject(engine, obj);

            obj._length = new PropertyDescriptor(1, PropertyFlag.Configurable);

            // The initial value of Array.prototype is the Array prototype object
            obj._prototypeDescriptor = new PropertyDescriptor(obj.PrototypeObject, PropertyFlag.AllForbidden);

            return obj;
        }

        protected override void Initialize()
        {
            _properties = new StringDictionarySlim<PropertyDescriptor>(5)
            {
                [GlobalSymbolRegistry.Species] = new GetSetPropertyDescriptor(get: new ClrFunctionInstance(Engine, "get [Symbol.species]", Species, 0, PropertyFlag.Configurable), set: Undefined,PropertyFlag.Configurable),
                ["from"] = new PropertyDescriptor(new PropertyDescriptor(new ClrFunctionInstance(Engine, "from", From, 1, PropertyFlag.Configurable), PropertyFlag.NonEnumerable)),
                ["isArray"] = new PropertyDescriptor(new PropertyDescriptor(new ClrFunctionInstance(Engine, "isArray", IsArray, 1), PropertyFlag.NonEnumerable)),
                ["of"] = new PropertyDescriptor(new PropertyDescriptor(new ClrFunctionInstance(Engine, "of", Of, 0, PropertyFlag.Configurable), PropertyFlag.NonEnumerable))
            };
        }

        private JsValue From(JsValue thisObj, JsValue[] arguments)
        {
            var source = arguments.At(0);
            var mapFunction = arguments.At(1);
            var callable = !mapFunction.IsUndefined() ? GetCallable(mapFunction) : null;
            var thisArg = arguments.At(2);

            if (source.IsNullOrUndefined())
            {
                ExceptionHelper.ThrowTypeError(_engine, "Cannot convert undefined or null to object");
            }

            if (source is JsString jsString)
            {
                var a = _engine.Array.ConstructFast((uint) jsString.Length);
                for (int i = 0; i < jsString._value.Length; i++)
                {
                    a.SetIndexValue((uint) i, JsString.Create(jsString._value[i]), updateLength: false);
                }
                return a;
            }

            if (thisObj.IsNull() || !(source is ObjectInstance objectInstance))
            {
                return _engine.Array.ConstructFast(0);
            }

            if (objectInstance.IsArrayLike)
            {
                return ConstructArrayFromArrayLike(objectInstance, callable, thisArg);
            }

            if (objectInstance is IObjectWrapper wrapper && wrapper.Target is IEnumerable enumerable)
            {
                return ConstructArrayFromIEnumerable(enumerable);
            }

            var instance = _engine.Array.ConstructFast(0);
            if (objectInstance.TryGetIterator(_engine, out var iterator))
            {
                var protocol = new ArrayProtocol(_engine, thisArg, instance, iterator, callable);
                protocol.Execute();
            }

            return instance;
        }

        private ArrayInstance ConstructArrayFromArrayLike(
            ObjectInstance objectInstance, 
            ICallable callable, 
            JsValue thisArg)
        {
            var operations = ArrayOperations.For(objectInstance);

            var length = operations.GetLength();

            var a = _engine.Array.ConstructFast(length);
            var args = !ReferenceEquals(callable, null)
                ? _engine._jsValueArrayPool.RentArray(2)
                : null;

            uint n = 0;
            for (uint i = 0; i < length; i++)
            {
                JsValue jsValue;
                operations.TryGetValue(i, out var value);
                if (!ReferenceEquals(callable, null))
                {
                    args[0] = value;
                    args[1] = i;
                    jsValue = callable.Call(thisArg, args);

                    // function can alter data
                    length = operations.GetLength();
                }
                else
                {
                    jsValue = value;
                }

                a.SetIndexValue(i, jsValue, updateLength: false);
                n++;
            }

            if (!ReferenceEquals(callable, null))
            {
                _engine._jsValueArrayPool.ReturnArray(args);
            }

            a.SetLength(length);
            return a;
        }

        internal sealed class ArrayProtocol : IteratorProtocol
        {
            private readonly JsValue _thisArg;
            private readonly ArrayInstance _instance;
            private readonly ICallable _callable;
            private long _index = -1;

            public ArrayProtocol(
                Engine engine, 
                JsValue thisArg,
                ArrayInstance instance,
                IIterator iterator,
                ICallable callable) : base(engine, iterator, 2)
            {
                _thisArg = thisArg;
                _instance = instance;
                _callable = callable;
            }

            protected override void ProcessItem(JsValue[] args, JsValue currentValue)
            {
                _index++;
                var sourceValue = ExtractValueFromIteratorInstance(currentValue);
                JsValue jsValue;
                if (!ReferenceEquals(_callable, null))
                {
                    args[0] = sourceValue;
                    args[1] = _index; 
                    jsValue = _callable.Call(_thisArg, args);
                }
                else
                {
                    jsValue = sourceValue;
                }

                _instance.SetIndexValue((uint) _index, jsValue, updateLength: false);
            }

            protected override void IterationEnd()
            {
                _instance.SetLength((uint) (_index + 1));
            }
        }

        private JsValue Of(JsValue thisObj, JsValue[] arguments)
        {
            var len = arguments.Length;
            ObjectInstance a;
            if (!ReferenceEquals(thisObj, this)
                && thisObj.IsConstructor)
            {
                a = ((IConstructor) thisObj).Construct(arguments, thisObj);

                for (uint k = 0; k < arguments.Length; k++)
                {
                    var kValue = arguments[k];
                    var key = TypeConverter.ToString(k);
                    a.CreateDataPropertyOrThrow(key, kValue);
                }

                a.Set(KnownKeys.Length, len, true);
            }
            else
            {
                // faster for real arrays
                ArrayInstance ai;
                a = ai = _engine.Array.Construct(len);

                for (uint k = 0; k < arguments.Length; k++)
                {
                    var kValue = arguments[k];
                    ai.SetIndexValue(k, kValue, updateLength: false);
                }

                ai.SetLength((uint) arguments.Length);
            }

            return a;
        }

        private static JsValue Species(JsValue thisObject, JsValue[] arguments)
        {
            return thisObject;
        }

        private static JsValue IsArray(JsValue thisObj, JsValue[] arguments)
        {
            var o = arguments.At(0);

            return IsArray(o);
        }

        private static JsValue IsArray(JsValue o)
        {
            if (!o.IsObject())
            {
                return JsBoolean.False;
            }

            if (o.AsObject().Class == "Array")
            {
                return JsBoolean.True;
            }

            if (o.AsObject().Class == "Proxy")
            {
                var proxyInstance = (ProxyInstance) o;
                proxyInstance.AssertNotRevoked("isArray");
                return IsArray(proxyInstance._target);
            }

            return JsBoolean.False;
        }

        public override JsValue Call(JsValue thisObject, JsValue[] arguments)
        {
            return Construct(arguments, thisObject);
        }

        public ObjectInstance Construct(JsValue[] arguments)
        {
            return Construct(arguments, this);
        }

        public ObjectInstance Construct(JsValue[] arguments, JsValue newTarget)
        {
            // check if we can figure out good size
            var capacity = arguments.Length > 0 ? (uint) arguments.Length : 0;
            if (arguments.Length == 1 && arguments[0].IsNumber())
            {
                var number = ((JsNumber) arguments[0])._value;
                if (number > 0)
                {
                    capacity = (uint) number;
                }
            }
            return Construct(arguments, capacity);
        }

        public ArrayInstance Construct(int capacity)
        {
            if (capacity < 0)
            {
                ExceptionHelper.ThrowArgumentException("invalid array length", nameof(capacity));
            }
            return Construct(System.ArrayExt.Empty<JsValue>(), (uint) capacity);
        }

        public ArrayInstance Construct(uint capacity)
        {
            return Construct(System.ArrayExt.Empty<JsValue>(), capacity);
        }

        public ArrayInstance Construct(JsValue[] arguments, uint capacity)
        {
            var instance = new ArrayInstance(Engine, capacity);
            instance._prototype = PrototypeObject;

            if (arguments.Length == 1 && arguments.At(0).IsNumber())
            {
                var length = TypeConverter.ToUint32(arguments.At(0));
                if (((JsNumber) arguments[0])._value != length)
                {
                    ExceptionHelper.ThrowRangeError(_engine, "Invalid array length");
                }

                instance._length = new PropertyDescriptor(length, PropertyFlag.OnlyWritable);
            }
            else if (arguments.Length == 1 && arguments[0] is IObjectWrapper objectWrapper)
            {
                if (objectWrapper.Target is IEnumerable enumerable)
                {
                    return ConstructArrayFromIEnumerable(enumerable);
                }
            }
            else if (arguments.Length == 1 && arguments[0] is ArrayInstance arrayInstance)
            {
                // direct copy
                return ConstructArrayFromArrayLike(arrayInstance, null, this);
            }
            else
            {
                instance._length = new PropertyDescriptor(0, PropertyFlag.OnlyWritable);
                if (arguments.Length > 0)
                {
                    PrototypeObject.Push(instance, arguments);
                }
            }

            return instance;
        }

        private ArrayInstance ConstructArrayFromIEnumerable(IEnumerable enumerable)
        {
            var jsArray = (ArrayInstance) Construct(Arguments.Empty);
            var tempArray = _engine._jsValueArrayPool.RentArray(1);
            foreach (var item in enumerable)
            {
                var jsItem = FromObject(Engine, item);
                tempArray[0] = jsItem;
                Engine.Array.PrototypeObject.Push(jsArray, tempArray);
            }

            _engine._jsValueArrayPool.ReturnArray(tempArray);
            return jsArray;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ArrayInstance ConstructFast(uint length)
        {
            var instance = new ArrayInstance(Engine, length)
            {
                _prototype = PrototypeObject,
                _length = new PropertyDescriptor(length, PropertyFlag.OnlyWritable)
            };
            return instance;
        }

        public ArrayInstance ArraySpeciesCreate(ObjectInstance originalArray, uint length)
        {
            var isArray = originalArray.IsArray();
            if (!isArray)
            {
                return ConstructFast(length);
            }

            var c = originalArray.Get("constructor");
            // If IsConstructor(C) is true, then
            // Let thisRealm be the current Realm Record.
            // Let realmC be ? GetFunctionRealm(C).
            // If thisRealm and realmC are not the same Realm Record, then
            // If SameValue(C, realmC.[[Intrinsics]].[[%Array%]]) is true, set C to undefined.

            if (c is ObjectInstance oi)
            {
                c = oi.Get(GlobalSymbolRegistry.Species) ?? Undefined;
            }


            if (c.IsUndefined())
            {
                return ConstructFast(length);
            }

            if (!c.IsConstructor)
            {
                ExceptionHelper.ThrowTypeError(_engine);
            }

            return Construct(length);
        }

        internal JsValue CreateArrayFromList(List<JsValue> values)
        {
            var jsArray = ConstructFast((uint) values.Count);
            var index = 0;
            for (; index < values.Count; index++)
            {
                var item = values[index];
                jsArray.SetIndexValue((uint) index, item, false);
            }

            jsArray.SetLength((uint) index);
            return jsArray;
        }
    }
}
