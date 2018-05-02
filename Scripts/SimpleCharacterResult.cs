using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib.Utils;

public struct SimpleCharacterResult
{
    public Vector3 position;
    public Quaternion rotation;
    public float timestamp;
}

public class SimpleCharacterResultField : LiteNetLibNetField<SimpleCharacterResult>
{
    public override void Deserialize(NetDataReader reader)
    {
        var result = new SimpleCharacterResult();
        result.position = new Vector3((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
        result.rotation = new Quaternion((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
        result.timestamp = (float)reader.GetShort() * 0.01f;
        Value = result;
    }

    public override bool IsValueChanged(SimpleCharacterResult newValue)
    {
        return !newValue.Equals(Value);
    }

    public override void Serialize(NetDataWriter writer)
    {
        writer.Put((short)(Value.position.x * 100));
        writer.Put((short)(Value.position.y * 100));
        writer.Put((short)(Value.position.z * 100));
        writer.Put((short)(Value.rotation.x * 100));
        writer.Put((short)(Value.rotation.y * 100));
        writer.Put((short)(Value.rotation.z * 100));
        writer.Put((short)(Value.rotation.w * 100));
        writer.Put((short)(Value.timestamp * 100));
    }
}
