// Plain C# mirror of economy.proto.
// Use this to wire up UI and gameplay logic right away.
// Swap for generated Google.Protobuf/protobuf-net types once the
// protoc pipeline is set up — field names/shape match 1:1 so the
// migration is mechanical.
//
// [MessagePackObject]/[Key] added as a temporary serialization stub so
// SectStateQueryHandler can produce real bytes for SectStateSnapshot before
// protoc codegen exists (see ToByteArray()/FromByteArray() at the bottom).
// Once economy.proto is compiled for real, EconomyStateProtobuf should hold
// actual protobuf bytes instead and this MessagePack path goes away.

using System;
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect
{
    public enum OwnerScope { Unspecified, Personal, SectStockpile }

    public enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }

    [MessagePackObject]
    public class CurrencyWallet
    {
        [Key(0)] public long SpiritStones { get; set; }
        [Key(1)] public long Contribution { get; set; }
    }

    [MessagePackObject]
    public class InventoryItem
    {
        [Key(0)] public string ItemDefId { get; set; }
        [Key(1)] public int Quantity { get; set; }
        [Key(2)] public int Grade { get; set; }          // 1-5
        [Key(3)] public OwnerScope OwnerScope { get; set; }
    }

    [MessagePackObject]
    public class DiscipleState
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string DisplayName { get; set; }
        [Key(2)] public DiscipleRank Rank { get; set; }
        [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
        [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
        [Key(5)] public string CurrentTask { get; set; }
    }

    [MessagePackObject]
    public class SectStockpile
    {
        [Key(0)] public Dictionary<string, int> RawResources { get; set; } = new Dictionary<string, int>();
        [Key(1)] public List<InventoryItem> CraftedGoods { get; set; } = new List<InventoryItem>();
    }

    [MessagePackObject]
    public class SectEconomyState
    {
        [Key(0)] public List<DiscipleState> Disciples { get; set; } = new List<DiscipleState>();
        [Key(1)] public SectStockpile Stockpile { get; set; } = new SectStockpile();

        // Temporary stand-ins for the protobuf .ToByteArray()/.Parser.ParseFrom()
        // API that will exist once economy.proto is actually compiled.
        public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);

        public static SectEconomyState FromByteArray(byte[] bytes) =>
            MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
    }
}

