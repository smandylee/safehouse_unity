using System;
using System.Collections.Generic;
using System.Linq;
using Safehouse.Core;
using Safehouse.Data;
using UnityEngine.UIElements;

namespace Safehouse.UI
{
    /// <summary>
    /// Creates a character and stores the Gundog Revised sheet. The numbers are not used by expeditions.
    /// </summary>
    [UnityEngine.RequireComponent(typeof(UIDocument))]
    public sealed class CharacterScreenController : UnityEngine.MonoBehaviour
    {
        private VisualElement _root;
        private ScrollView _scroll;
        private Label _error;
        private readonly Dictionary<string, int> _abilities =
            Gundog.Abilities.ToDictionary(stat => stat.StatId, stat => Gundog.AbilityDefault);
        private readonly Dictionary<string, TextField> _identity = new Dictionary<string, TextField>();
        private readonly List<TextField> _career = new List<TextField>();
        private readonly HashSet<string> _arts = new HashSet<string>();
        private TextField _name;
        private string _main = "assault";
        private string _sub = "scout";
        private VisualElement _skills;
        private VisualElement _artRow;
        private VisualElement _mainRow;
        private VisualElement _subRow;
        private int _reward;
        private int _movement = 9;
        private int _durability = 48;
        private Label _rewardLabel;
        private Label _movementLabel;
        private Label _durabilityLabel;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _scroll = _root.Q<ScrollView>("character-scroll");
            _error = _root.Q<Label>("character-error");
            _root.Q<Button>("navtab-gear").clicked += () => ScreenNavigator.Go("gear");
            _root.Q<Button>("navtab-hideout").clicked += () => ScreenNavigator.Go("hideout");
            _root.Q<Button>("navtab-traders").clicked += () => ScreenNavigator.Go("traders");
            _root.Q<Button>("navtab-scavenge").clicked += () => ScreenNavigator.Go("scavenge");
            _root.Q<Button>("navtab-settings").clicked += () => ScreenNavigator.Go("settings");
            ScreenNavigator.Register("character", Show, Hide);
            Hide();
        }

        private void OnDisable() => ScreenNavigator.Unregister("character");

        private void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            Build();
        }

        private void Hide() => _root.style.display = DisplayStyle.None;

        private void Build()
        {
            _scroll.Clear();
            _error.text = "";
            _identity.Clear();
            _career.Clear();
            var column = new VisualElement();
            column.Add(Section("NAME"));
            _name = Field("NAME", wide: true);
            column.Add(_name);

            column.Add(Section("IDENTITY"));
            var identity = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            AddIdentity(identity, "nationality", "국적");
            AddIdentity(identity, "gender", "성별");
            AddIdentity(identity, "age", "나이");
            AddIdentity(identity, "blood_type", "혈액형");
            AddIdentity(identity, "height", "신장");
            AddIdentity(identity, "weight", "체중");
            AddIdentity(identity, "hair_color", "머리색");
            AddIdentity(identity, "eye_color", "눈");
            AddIdentity(identity, "skin_color", "피부");
            AddIdentity(identity, "rank", "계급");
            AddIdentity(identity, "language", "언어");
            AddIdentity(identity, "occupation", "직업");
            AddIdentity(identity, "era", "시대");
            column.Add(identity);

            column.Add(Section("ABILITY"));
            foreach (var stat in Gundog.Abilities)
            {
                column.Add(AbilityRow(stat));
            }

            column.Add(Section("CLASS"));
            column.Add(new Label("MAIN") { style = { marginBottom = 4 } });
            _mainRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            column.Add(_mainRow);
            column.Add(new Label("SUB") { style = { marginTop = 4, marginBottom = 4 } });
            _subRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            column.Add(_subRow);

            column.Add(Section("SKILL"));
            var note = new Label("클래스 보정입니다. 원정에는 적용되지 않습니다.");
            note.AddToClassList("text-muted");
            note.style.marginBottom = 6;
            column.Add(note);
            _skills = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            column.Add(_skills);

            column.Add(Section("CLASS ARTS"));
            _artRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            column.Add(_artRow);

            column.Add(Section("CAREER"));
            for (var index = 0; index < Gundog.CareerLines; index++)
            {
                var line = Field("경력 " + (index + 1), wide: true);
                _career.Add(line);
                column.Add(line);
            }

            column.Add(Section("SHEET"));
            _rewardLabel = new Label();
            _movementLabel = new Label();
            _durabilityLabel = new Label();
            column.Add(Stepper("리워드", () => _reward, value => _reward = value, 0, Gundog.MaxReward, _rewardLabel));
            column.Add(Stepper("이동력", () => _movement, value => _movement = value, 0, Gundog.MaxMovement, _movementLabel));
            column.Add(Stepper("내구력", () => _durability, value => _durability = value, 0, Gundog.MaxDurability, _durabilityLabel));

            var create = new Button(Create) { text = "CREATE" };
            create.AddToClassList("btn");
            create.AddToClassList("btn-primary");
            create.style.marginTop = 12;
            create.style.width = 160;
            column.Add(create);
            _scroll.Add(column);
            RefreshClasses();
        }

        private void Create()
        {
            var session = GearScreenController.Current?.Session;
            if (session == null || !session.Saves)
            {
                _error.text = "Open a save folder on the GEAR screen first.";
                return;
            }

            try
            {
                var bio = CharacterSheet.BioFields.ToDictionary(field => field, field => "");
                foreach (var pair in _identity)
                {
                    if (bio.ContainsKey(pair.Key))
                    {
                        bio[pair.Key] = pair.Value.value.Trim();
                    }
                }

                var sheet = new GundogSheet(_abilities, _main, _sub, _arts.OrderBy(id => id, StringComparer.Ordinal),
                    _career.Select(field => field.value.Trim()).ToList(), _reward, _movement, _durability,
                    Text("rank"), Text("language"), Text("occupation"), Text("era"));
                session.CreateCharacter(_name.value, bio, sheet);
                _error.text = "";
                ScreenNavigator.Go("gear");
            }
            catch (Exception error)
            {
                _error.text = error.Message;
            }
        }

        private string Text(string key) => _identity.ContainsKey(key) ? _identity[key].value.Trim() : "";

        private void RefreshClasses()
        {
            FillClassRow(_mainRow, true);
            FillClassRow(_subRow, false);
            _skills.Clear();
            var sheet = new GundogSheet(_abilities, _main, _sub, null,
                Enumerable.Repeat("", Gundog.CareerLines).ToList(), 0, 0, 0, "", "", "", "");
            var modifiers = sheet.SkillModifiers();
            foreach (var skill in Gundog.Skills)
            {
                var label = new Label(skill.Name + "  " + modifiers[skill.StatId]);
                label.style.marginRight = 14;
                label.AddToClassList("text-accent");
                _skills.Add(label);
            }

            var chosenClasses = new[] { Gundog.FindClass(_main), Gundog.FindClass(_sub) }
                .GroupBy(entry => entry.ClassId)
                .Select(group => group.First())
                .ToList();
            var allowed = new HashSet<string>();
            foreach (var entry in chosenClasses)
            {
                foreach (var art in entry.Arts)
                {
                    allowed.Add(art.ArtId);
                }
            }

            _arts.RemoveWhere(id => !allowed.Contains(id));
            _artRow.Clear();
            foreach (var entry in chosenClasses)
            {
                foreach (var art in entry.Arts)
                {
                    var id = art.ArtId;
                    var button = new Button(() =>
                    {
                        if (!_arts.Add(id))
                        {
                            _arts.Remove(id);
                        }

                        RefreshClasses();
                    })
                    { text = art.Name };
                    button.AddToClassList("btn");
                    button.AddToClassList("btn-outline");
                    button.AddToClassList("sheet-art");
                    if (_arts.Contains(id))
                    {
                        button.AddToClassList("sheet-art-on");
                    }

                    _artRow.Add(button);
                }
            }
        }

        private void FillClassRow(VisualElement row, bool main)
        {
            row.Clear();
            foreach (var entry in Gundog.Classes)
            {
                var id = entry.ClassId;
                var button = new Button(() =>
                {
                    if (main)
                    {
                        _main = id;
                    }
                    else
                    {
                        _sub = id;
                    }

                    RefreshClasses();
                })
                { text = entry.Name };
                button.AddToClassList("btn");
                button.AddToClassList((main ? _main : _sub) == id ? "btn-primary" : "btn-outline");
                button.AddToClassList("sheet-class");
                row.Add(button);
            }
        }

        private VisualElement AbilityRow(GundogStat stat)
        {
            var row = new VisualElement();
            row.AddToClassList("sheet-row");
            var name = new Label(stat.Name);
            name.AddToClassList("sheet-stat");
            var value = new Label(_abilities[stat.StatId].ToString());
            value.AddToClassList("sheet-value");
            value.AddToClassList("text-mono");
            row.Add(name);
            row.Add(Step("-", () => ChangeAbility(stat.StatId, -1, value)));
            row.Add(value);
            row.Add(Step("+", () => ChangeAbility(stat.StatId, 1, value)));
            return row;
        }

        private void ChangeAbility(string statId, int delta, Label value)
        {
            var next = _abilities[statId] + delta;
            if (next < Gundog.AbilityMin || next > Gundog.AbilityMax)
            {
                return;
            }

            _abilities[statId] = next;
            value.text = next.ToString();
        }

        private VisualElement Stepper(string caption, Func<int> read, Action<int> write, int low, int high, Label value)
        {
            var row = new VisualElement();
            row.AddToClassList("sheet-row");
            var name = new Label(caption);
            name.AddToClassList("sheet-stat");
            value.text = read().ToString();
            value.AddToClassList("sheet-value");
            value.AddToClassList("text-mono");
            row.Add(name);
            row.Add(Step("-", () =>
            {
                if (read() > low)
                {
                    write(read() - 1);
                    value.text = read().ToString();
                }
            }));
            row.Add(value);
            row.Add(Step("+", () =>
            {
                if (read() < high)
                {
                    write(read() + 1);
                    value.text = read().ToString();
                }
            }));
            return row;
        }

        private static Button Step(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("btn");
            button.AddToClassList("btn-outline");
            button.AddToClassList("sheet-step");
            return button;
        }

        private void AddIdentity(VisualElement row, string key, string label)
        {
            var field = Field(label);
            _identity[key] = field;
            row.Add(field);
        }

        private static TextField Field(string label, bool wide = false)
        {
            var field = new TextField(label);
            field.AddToClassList("sheet-field");
            if (wide)
            {
                field.AddToClassList("sheet-field-wide");
            }

            field.maxLength = wide ? CharacterSheet.MaxNameLength : Gundog.MaxLineLength;
            var caption = field.labelElement;
            caption.style.minWidth = 0;
            caption.style.width = StyleKeyword.Auto;
            caption.style.marginRight = 6;
            caption.style.paddingLeft = 0;
            caption.style.paddingRight = 0;
            caption.style.flexGrow = 0;
            caption.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;
            field.style.width = wide ? new StyleLength(new Length(100, LengthUnit.Percent)) : StyleKeyword.Auto;
            field.style.flexGrow = wide ? 1 : 0;
            var input = field.Q(className: "unity-base-field__input");
            if (input != null)
            {
                input.style.backgroundColor = UnityEngine.Color.white;
                input.style.color = new UnityEngine.Color(20f / 255f, 23f / 255f, 26f / 255f);
            }

            return field;
        }

        private static Label Section(string text)
        {
            var label = new Label(text);
            label.AddToClassList("text-section");
            label.AddToClassList("sheet-section");
            label.style.marginTop = 8;
            return label;
        }
    }
}
