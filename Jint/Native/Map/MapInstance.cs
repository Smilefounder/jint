﻿using System.Runtime.CompilerServices;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace Jint.Native.Map
{
    public class MapInstance : ObjectInstance
    {
        internal readonly OrderedDictionary<JsValue, JsValue> _map;

        public MapInstance(Engine engine)
            : base(engine, objectClass: "Map")
        {
            _map = new OrderedDictionary<JsValue, JsValue>();
        }

        public override PropertyDescriptor GetOwnProperty(in Key propertyName)
        {
            if (propertyName == KnownKeys.Size)
            {
                return new PropertyDescriptor(_map.Count, PropertyFlag.None);
            }

            return base.GetOwnProperty(propertyName);
        }

        protected override bool TryGetProperty(in Key propertyName, out PropertyDescriptor descriptor)
        {
            if (propertyName == KnownKeys.Size)
            {
                descriptor = new PropertyDescriptor(_map.Count, PropertyFlag.None);
                return true;
            }

            return base.TryGetProperty(propertyName, out descriptor);
        }

        public void Clear()
        {
            _map.Clear();
        }

        public bool Has(JsValue key)
        {
            return _map.ContainsKey(key);
        }

        public bool Delete(JsValue key)
        {
            return _map.Remove(key);
        }

        public void Set(JsValue key, JsValue value)
        {
            _map[key] = value;
        }

        public void ForEach(ICallable callable, JsValue thisArg)
        {
            var args = _engine._jsValueArrayPool.RentArray(3);
            args[2] = this;

            for (var i = 0; i < _map.Count; i++)
            {
                args[0] = _map[i];
                args[1] = _map.GetKey(i);
                callable.Call(thisArg, args);
            }

            _engine._jsValueArrayPool.ReturnArray(args);
        }

        public JsValue Get(JsValue key)
        {
            if (!_map.TryGetValue(key, out var value))
            {
                return Undefined;
            }

            return value;
        }

        public ObjectInstance Iterator()
        {
            return _engine.Iterator.Construct(this);
        }

        public ObjectInstance Keys()
        {
            return _engine.Iterator.Construct(_map.Keys);
        }

        public ObjectInstance Values()
        {
            return _engine.Iterator.Construct(_map.Values);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint GetSize()
        {
            return (uint) _map.Count;
        }
    }
}
