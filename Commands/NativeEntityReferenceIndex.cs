using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands
{
    // Capture-local index. A SOLIDWORKS entity wrapper's IUnknown address is not
    // a persistent B-rep identity across traversal APIs. Resolve aliases ONLY
    // through native persistent references or unique kernel object equality.
    // No coordinate/length/proximity matching; no omitted edge or face.
    internal sealed class NativeEntityReferenceIndex<T> where T:class
    {
        private readonly T[] source;
        private readonly Func<T,long> runtime;
        private readonly Func<T,string> persistent;
        private readonly Func<T,T,bool> same;
        private readonly Dictionary<long,int> byRuntime=new Dictionary<long,int>();
        private Dictionary<string,int> byPersistent;
        internal int PersistentResolutions {get;private set;}
        internal int KernelResolutions {get;private set;}
        internal NativeEntityReferenceIndex(IEnumerable<T> source,Func<T,long> runtime,Func<T,string> persistent,Func<T,T,bool> same=null)
        {
            this.source=source.ToArray();this.runtime=runtime;this.persistent=persistent;this.same=same;
            for(int i=0;i<this.source.Length;i++)
            {
                if(this.source[i]==null||byRuntime.ContainsKey(runtime(this.source[i])))throw new InvalidOperationException("NATIVE_DUPLICATE_ENTITY_IDENTITY");
                byRuntime.Add(runtime(this.source[i]),i);
            }
        }
        internal int Resolve(T entity)
        {
            if(entity==null)throw new InvalidOperationException("NATIVE_MISSING_TOPOLOGY_ENTITY");
            int index;if(byRuntime.TryGetValue(runtime(entity),out index))return index;
            if(byPersistent==null)
            {
                byPersistent=new Dictionary<string,int>(StringComparer.Ordinal);
                for(int i=0;i<source.Length;i++)
                {
                    string key=persistent(source[i]);if(string.IsNullOrEmpty(key))continue;
                    if(byPersistent.ContainsKey(key))throw new InvalidOperationException("NATIVE_AMBIGUOUS_PERSISTENT_ENTITY_IDENTITY");
                    byPersistent.Add(key,i);
                }
            }
            string id=persistent(entity);
            if(!string.IsNullOrEmpty(id)&&byPersistent.TryGetValue(id,out index)){PersistentResolutions++;return index;}
            if(same!=null)
            {
                int found=-1;
                for(int i=0;i<source.Length;i++)if(same(entity,source[i]))
                {if(found>=0)throw new InvalidOperationException("NATIVE_AMBIGUOUS_KERNEL_ENTITY_IDENTITY");found=i;}
                if(found>=0){KernelResolutions++;return found;}
            }
            // Do not cache transient wrapper pointers: a later wrapper can
            // reuse their address after collection. Originals remain held here.
            throw new InvalidOperationException("NATIVE_TOPOLOGY_ENTITY_IDENTITY_UNRESOLVED");
        }
    }
}
