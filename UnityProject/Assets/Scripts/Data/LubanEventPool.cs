using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Luban.SimpleJSON;

namespace Xianxia.Sect
{
    // Runtime wrapper around the Luban-generated "game" module tables
    // (cfg.game.*). Registered as a singleton by reference in
    // GameLifetimeScope (builder.RegisterInstance(new LubanEventPool())),
    // same pattern as before the xlsx -> csv pipeline migration.
    public class LubanEventPool
    {
        private readonly cfg.Tables _tables;
        private readonly Dictionary<string, List<cfg.game.EventChoiceDef>> _choicesByEventId;

        public LubanEventPool()
        {
            _tables = new cfg.Tables(LoadJson);
            _choicesByEventId = _tables.TbEventChoiceDef.DataList
                .GroupBy(c => c.EventId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // Direct access to the generated tables, mirroring cfg.game naming.
        public cfg.game.TbEventDef TbEventDef => _tables.TbEventDef;
        public cfg.game.TbEventChoiceDef TbEventChoiceDef => _tables.TbEventChoiceDef;

        // Luban's cs-simple-json target names each data file after
        // "<module>_<table>" (lowercase), so we load "game_tbeventdef" /
        // "game_tbeventchoicedef" from Resources/DataTables.
        private static JSONNode LoadJson(string file)
        {
            var textAsset = Resources.Load<TextAsset>($"DataTables/{file}");
            if (textAsset == null)
            {
                Debug.LogError($"[LubanEventPool] Missing generated data file 'DataTables/{file}' " +
                                "in Resources - did you run DataTables/gen.sh (or gen.bat)?");
                return null;
            }
            return JSON.Parse(textAsset.text);
        }

        public cfg.game.EventDef GetRandomEvent()
        {
            var events = _tables.TbEventDef.DataList;
            if (events.Count == 0)
            {
                Debug.LogWarning("[LubanEventPool] TbEventDef is empty - check EventDef.csv has rows and gen.sh ran successfully.");
                return null;
            }
            var totalWeight = events.Sum(e => Mathf.Max(0f, e.Weight));
            if (totalWeight <= 0f)
            {
                return events[Random.Range(0, events.Count)];
            }
            var roll = Random.Range(0f, totalWeight);
            var cumulative = 0f;
            foreach (var eventDef in events)
            {
                cumulative += Mathf.Max(0f, eventDef.Weight);
                if (roll <= cumulative) return eventDef;
            }
            return events[^1];
        }

        public IReadOnlyList<cfg.game.EventChoiceDef> GetChoices(string eventId)
        {
            return _choicesByEventId.TryGetValue(eventId, out var list)
                ? list
                : new List<cfg.game.EventChoiceDef>();
        }
    }
}
