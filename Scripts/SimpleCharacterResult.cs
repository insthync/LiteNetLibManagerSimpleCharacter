using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib.Utils;

public struct SimpleCharacterResult : INetSerializable
{
    public Vector3 position;
    public Quaternion rotation;
    public float timestamp;

    public void Deserialize(NetDataReader reader)
    {
        position = new Vector3((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
        rotation = new Quaternion((float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f, (float)reader.GetShort() * 0.01f);
        timestamp = (float)reader.GetShort() * 0.01f;
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((short)(position.x * 100));
        writer.Put((short)(position.y * 100));
        writer.Put((short)(position.z * 100));
        writer.Put((short)(rotation.x * 100));
        writer.Put((short)(rotation.y * 100));
        writer.Put((short)(rotation.z * 100));
        writer.Put((short)(rotation.w * 100));
        writer.Put((short)(timestamp * 100));
    }
}

public class SimpleCharacterResultField : LiteNetLibSyncField<SimpleCharacterResult>
{
    protected override bool IsValueChanged(SimpleCharacterResult newValue)
    {
        return !newValue.Equals(Value);
    }
}
