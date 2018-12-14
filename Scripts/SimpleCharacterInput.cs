using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib.Utils;

public struct SimpleCharacterInput : INetSerializable
{
    public float horizontal;
    public float vertical;
    public bool isJump;
    public float timestamp;

    public void Deserialize(NetDataReader reader)
    {
        horizontal = (float)reader.GetShort() * 0.01f;
        vertical = (float)reader.GetShort() * 0.01f;
        isJump = reader.GetBool();
        timestamp = (float)reader.GetShort() * 0.01f;
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((short)(horizontal * 100));
        writer.Put((short)(vertical * 100));
        writer.Put(isJump);
        writer.Put((short)(timestamp * 100));
    }
}

public class SimpleCharacterInputField : LiteNetLibSyncField<SimpleCharacterInput>
{
    protected override bool IsValueChanged(SimpleCharacterInput newValue)
    {
        return !newValue.Equals(Value);
    }
}
