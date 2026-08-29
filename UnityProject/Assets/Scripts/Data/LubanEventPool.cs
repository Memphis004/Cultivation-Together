using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Luban.SimpleJSON; // อันนี้ถูกต้องแล้วค่ะ

namespace Xianxia.Sect
{
    public class LubanEventPool
    {
        private readonly cfg.Tables _tables;
        private readonly Dictionary<string, List<cfg.worldevent.EventChoiceRow>> _choicesByEventId;

        public LubanEventPool()
        {
            _tables = new cfg.Tables(LoadJson);
            _choicesByEventId = _tables.TbEventChoice.DataList
                .GroupBy(c => c.EventId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // แก้ตรงนี้ค่ะ: SimpleJSON.JSONNode -> JSONNode
        private static JSONNode LoadJson(string file)
        {
            var textAsset = Resources.Load<TextAsset>($"DataTables/{file}");
            if (textAsset == null)
            {
                Debug.LogError($"[LubanEventPool] Missing generated data file 'DataTables/{file}' " +
                                "in Resources - did you run DataTables/gen.sh (or gen.bat)?");
                return null;
            }
            // แก้ตรงนี้ค่ะ: SimpleJSON.JSON.Parse -> JSON.Parse
            return JSON.Parse(textAsset.text);
        }

        public cfg.worldevent.EventRow GetRandomEvent()
        {
            var events = _tables.TbEvent.DataList;
            if (events.Count == 0)
            {
                Debug.LogWarning("[LubanEventPool] TbEvent is empty - check event.xlsx has rows and gen.sh ran successfully.");
                return null;
            }
            var totalWeight = events.Sum(e => Mathf.Max(0f, e.Weight));
            if (totalWeight <= 0f)
            {
                return events[Random.Range(0, events.Count)];
            }
            var roll = Random.Range(0f, totalWeight);
            var cumulative = 0f;
            foreach (var eventRow in events)
            {
                cumulative += Mathf.Max(0f, eventRow.Weight);
                if (roll <= cumulative) return eventRow;
            }
            return events[^1];
        }

        public IReadOnlyList<cfg.worldevent.EventChoiceRow> GetChoices(string eventId)
        {
            return _choicesByEventId.TryGetValue(eventId, out var list)
                ? list
                : new List<cfg.worldevent.EventChoiceRow>();
        }
    }
}