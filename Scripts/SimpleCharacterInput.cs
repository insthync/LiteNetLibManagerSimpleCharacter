using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibHighLevel;
using LiteNetLib.Utils;

public struct SimpleCharacterInput
{
    public float horizontal;
    public float vertical;
    public bool isJump;
    public float timestamp;
}

public class SimpleCharacterInputField : LiteNetLibNetField<SimpleCharacterInput>
{
    public override void Deserialize(NetDataReader reader)
    {
        var result = new SimpleCharacterInput();
        result.horizontal = (float)reader.GetShort() * 0.01f;
        result.vertical = (float)reader.GetShort() * 0.01f;
        result.isJump = reader.GetBool();
        result.timestamp = reader.GetFloat();
        Value = result;
    }

    public override bool IsValueChanged(SimpleCharacterInput newValue)
    {
        return !newValue.Equals(Value);
    }

    public override void Serialize(NetDataWriter writer)
    {
        writer.Put((short)(Value.horizontal * 100));
        writer.Put((short)(Value.vertical * 100));
        writer.Put(Value.isJump);
        writer.Put(Value.timestamp);
    }
}

public class SimpleCharacterInputSyncField : LiteNetLibSyncField<SimpleCharacterInputField, SimpleCharacterInput> { }
