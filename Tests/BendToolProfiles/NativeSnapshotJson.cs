using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

// Test comparison only. JSON object property order is not semantic; array
// order, every field and exact numeric values remain part of the comparison.
internal static class NativeSnapshotJson
{
    internal static string Canonical(object value)
    {
        var json=new JavaScriptSerializer {MaxJsonLength=int.MaxValue};
        return Encode(json.DeserializeObject(json.Serialize(value)),json);
    }
    private static string Encode(object value,JavaScriptSerializer json)
    {
        var map=value as IDictionary<string,object>;
        if(map!=null)return "{"+string.Join(",",map.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>json.Serialize(k)+":"+Encode(map[k],json)))+"}";
        var sequence=value as IEnumerable;
        if(sequence!=null&&!(value is string))return "["+string.Join(",",sequence.Cast<object>().Select(v=>Encode(v,json)))+"]";
        return json.Serialize(value);
    }
}
