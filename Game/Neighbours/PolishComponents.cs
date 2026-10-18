// Placemaker neighbourhood polish — what is saved with the city (GPL-3.0).
// Version-first ISerializable layouts (RESEARCH-NOTES rule 5, as Game/Groups/GroupComponents.cs): the serializer packs
// components back to back without lengths, so a reader consumes exactly what its version wrote; fields are only appended.
//
//   * PolishedLot on a growable building: the player's yes/no flag (the painter, "Polish new houses"). Only yes lots are
//     ever edited. Level-ups keep the entity (and the flag); a replaced building gets the flag back by position.
//   * PolishPiece on a surface or object the polish made for a house (bed, path, paving, owned lawn, bush, rock, shared
//     green): after a load the polish still knows which of the house's pieces are its own, so Unpolish takes them away.
//   * PolishLawnOriginal on one of the house's own lawns the polish reshaped: its nodes as the game made them, so Unpolish
//     (and a later plan) gives the lawn back exactly, also after a load.

using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public struct PolishedLot : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        public bool Yes;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Yes);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out Yes);
        }
    }

    /// <summary>Save format: never renumber.</summary>
    public enum PolishPieceKind : byte
    {
        /// <summary>Bed, path, paving, owned lawn, bush or rock: re-planned with the garden.</summary>
        Garden = 0,
        /// <summary>The shared green in the middle of a ring (and its bed): only a plan that sees the whole ring makes it.</summary>
        Common = 1,
    }

    public struct PolishPiece : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        public PolishPieceKind Kind;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write((byte)Kind);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out byte kind);
            Kind = (PolishPieceKind)kind;
        }
    }

    [InternalBufferCapacity(0)]
    public struct PolishLawnOriginal : IBufferElementData, ISerializable
    {
        public float3 Position;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Position);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Position);
    }
}
