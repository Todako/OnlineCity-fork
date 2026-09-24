using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    [StaticConstructorOnStartup]
    public class GeneralTexture
    {
        public const int UpdateSecondColonyScreen = 60;

        public static readonly Texture2D IconAddTex;
        public static readonly Texture2D IconDelTex;
        public static readonly Texture2D IconSubMenuTex;
        public static readonly Texture2D IconForums;
        public static readonly Texture2D IconSkull;
        public static readonly Texture2D TradeButtonIcon;
        public static readonly Texture2D Waypoint;
        public static readonly Texture2D Null;
        public static readonly Texture2D OCInfo;
        public static readonly Texture2D OCSystem;
        public static readonly Texture2D OCToChat;
        public static readonly Texture2D OCBug;
        public static readonly Texture2D IconHuman;
        public static readonly Texture2D OCE_To;
        public static readonly Texture2D OCE_Del;
        public static readonly Texture2D OCE_Sell;
        public static readonly Texture2D OCE_Trans;
        public static readonly Texture2D OCE_Swap;
        public static readonly Texture2D OCE_Add;
        public static readonly Texture2D Pawns;
        public static readonly Texture2D PawnsDown;
        public static readonly Texture2D PawnsBleed;
        public static readonly Texture2D PawnsNeedingTend;
        public static readonly Texture2D PawnsAnimal;
        public static readonly Texture2D ItemStash;
        public static readonly Texture2D AttackSettlement;
        public static readonly Texture2D OpenBox;
        public static readonly Texture2D Caravan;
        public static readonly Texture2D HomeAreaOn;
        public static readonly Texture2D RankingUp;
        public static readonly Texture2D RankingDown;
        public static readonly Texture2D BaseOnlineButtonShowMap;
        public static readonly Texture2D IncidentViewIcon;

        static GeneralTexture()
        {
            IconAddTex = ContentFinder<Texture2D>.Get("OCAdd");
            IconDelTex = ContentFinder<Texture2D>.Get("OCDel");
            IconSubMenuTex = ContentFinder<Texture2D>.Get("OCSubMenu");
            IconForums = ContentFinder<Texture2D>.Get("Forums");
            IconSkull = ContentFinder<Texture2D>.Get("Skull");
            TradeButtonIcon = ContentFinder<Texture2D>.Get("Trade");
            Waypoint = ContentFinder<Texture2D>.Get("Waypoint");
            Null = ContentFinder<Texture2D>.Get("Null");
            OCInfo = ContentFinder<Texture2D>.Get("OCInfo");
            OCSystem = ContentFinder<Texture2D>.Get("OCSystem");
            OCToChat = ContentFinder<Texture2D>.Get("OCToChat");
            OCBug = ContentFinder<Texture2D>.Get("OCBug");
            IconHuman = ContentFinder<Texture2D>.Get("IconHuman");
            OCE_To = ContentFinder<Texture2D>.Get("OCE_To");
            OCE_Del = ContentFinder<Texture2D>.Get("OCE_Del");
            OCE_Sell = ContentFinder<Texture2D>.Get("OCE_Sell");
            OCE_Trans = ContentFinder<Texture2D>.Get("OCE_Trans");
            OCE_Swap = ContentFinder<Texture2D>.Get("OCE_Swap");
            OCE_Add = ContentFinder<Texture2D>.Get("OCE_Add");
            Pawns = ContentFinder<Texture2D>.Get("Pawns");
            PawnsDown = ContentFinder<Texture2D>.Get("PawnsDown");
            PawnsBleed = ContentFinder<Texture2D>.Get("PawnsBleed");
            PawnsNeedingTend = ContentFinder<Texture2D>.Get("PawnsNeedingTend");
            PawnsAnimal = ContentFinder<Texture2D>.Get("PawnsAnimal");
            ItemStash = ContentFinder<Texture2D>.Get("ItemStash");
            AttackSettlement = ContentFinder<Texture2D>.Get("AttackSettlement");
            OpenBox = ContentFinder<Texture2D>.Get("OpenBox");
            Caravan = ContentFinder<Texture2D>.Get("Caravan");
            HomeAreaOn = ContentFinder<Texture2D>.Get("HomeAreaOn");
            RankingUp = ContentFinder<Texture2D>.Get("RankingUp");
            RankingDown = ContentFinder<Texture2D>.Get("RankingDown");
            BaseOnlineButtonShowMap = ContentFinder<Texture2D>.Get("ShowMap");
            IncidentViewIcon = ContentFinder<Texture2D>.Get("IncidentViewIcon");

            Clear();
        }

        private class TextureContainer
        {
            public DateTime LoadTime;
            public string Hash;
            public byte[] Data;
            public Texture2D _Texture;

            public Texture2D Texture
            {
                get
                {
                    if (_Texture != null) return _Texture;
                    if (Data == null || Data.Length == 0) return _Texture = Null;
                    return _Texture = GameUtils.GetTextureFromSaveData(Data);
                }
            }

            public TextureContainer() { }
            public TextureContainer(Texture2D texture)
            {
                _Texture = texture;
            }
        }

        public static GeneralTexture Get { get; private set; }

        private readonly ConcurrentDictionary<string, TextureContainer> LoadedTextures = new ConcurrentDictionary<string, TextureContainer>();
        private readonly Dictionary<string, DateTime> LoadedAgings = new Dictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, TextureContainer> LoadedOldTextures = new ConcurrentDictionary<string, TextureContainer>();

        // ОПТИМІЗАЦІЯ: O(1) структури для черг завантаження замість масивів List<string>
        private readonly object _syncLock = new object();
        private readonly HashSet<string> _queuedItems = new HashSet<string>();
        private readonly HashSet<string> _loadingNow = new HashSet<string>();
        private readonly Queue<string> _checkQueue = new Queue<string>();
        private readonly Queue<string> _downloadQueue = new Queue<string>();

        // Багаторазові буфери списків для ліквідації навантаження на Garbage Collector
        private readonly List<string> _checkNamesBuffer = new List<string>(100);
        private readonly List<ModelFileSharing> _checkMfsBuffer = new List<ModelFileSharing>(100);
        private readonly List<string> _expiredAgingsBuffer = new List<string>(32);

        private readonly ConcurrentDictionary<string, Def> GetDefs = new ConcurrentDictionary<string, Def>();
        private readonly ConcurrentDictionary<Def, Texture2D> GetDefTextures = new ConcurrentDictionary<Def, Texture2D>();

        public static void Clear()
        {
            Get = new GeneralTexture();
        }

        private static bool Inited = false;
        public static void Init()
        {
            Clear();
            if (Inited) return;
            Inited = true;
        }

        public Texture2D GetEmoji(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var cleanName = name.Trim().Trim(':');
            var path = "Emoji/Emoji_" + cleanName;

            if (!PanelText.GlobalImgs.TryGetValue(path, out var icon))
            {
                try
                {
                    icon = ContentFinder<Texture2D>.Get(path, false);
                }
                catch
                {
                    icon = null;
                }
                if (icon != null) PanelText.GlobalImgs[path] = icon;
            }
            return icon;
        }

        public Def GetDef(string defName) => defName == null ? null : GetDefs.GetOrAdd(defName, FindDefInternal);

        private static Def FindDefInternal(string name)
        {
            Def def = (ThingDef)GenDefDatabase.GetDefSilentFail(typeof(ThingDef), name, false);
            if (def == null) def = (WorldObjectDef)GenDefDatabase.GetDefSilentFail(typeof(WorldObjectDef), name, false);
            return def;
        }

        public Texture2D GetDefTexture(Def def) => def == null ? null : GetDefTextures.GetOrAdd(def, ResolveDefTextureInternal);

        private static Texture2D ResolveDefTextureInternal(Def def)
        {
            if (def is ThingDef thingDef)
            {
                return thingDef.GetUIIconForStuff(null);
            }
            if (def is WorldObjectDef defWO)
            {
                return defWO.ExpandingIconTexture ?? (Texture2D)(defWO.Material?.mainTexture);
            }
            return null;
        }

        public Texture2D GetDefTexture(string defName) => GetDefTexture(GetDef(defName));

        /// <summary>
        /// Повертає текстуру за її кодовим ім'ям (іконка гравця або скріншот бази).
        /// ОПТИМІЗАЦІЯ: швидка перевірка та додавання до черги за O(1) без алокацій пам'яті.
        /// </summary>
        public Texture2D ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Null;

            return LoadedTextures.GetOrAdd(name, n =>
            {
                lock (_syncLock)
                {
                    if (!_queuedItems.Contains(n) && !_loadingNow.Contains(n))
                    {
                        _queuedItems.Add(n);
                        _checkQueue.Enqueue(n);
                    }
                }

                return LoadedOldTextures.TryGetValue(n, out var res) ? res : new TextureContainer(Null);
            }).Texture;
        }

        public TimeSpan GetLoadTimeByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return TimeSpan.MaxValue;

            return LoadedTextures.TryGetValue(name, out var res)
                ? DateTime.UtcNow - res.LoadTime
                : LoadedOldTextures.TryGetValue(name, out res)
                ? DateTime.UtcNow - res.LoadTime
                : TimeSpan.MaxValue;
        }

        public bool IsNotDataByName(string name) => IsNotDataByLoadTime(GetLoadTimeByName(name));

        public bool IsNotDataByLoadTime(TimeSpan time) => time < TimeSpan.MaxValue && time > new TimeSpan(1, 0, 0, 0);

        public bool IsNotCheckByName(string name) => IsNotCheckByLoadTime(GetLoadTimeByName(name));

        public bool IsNotCheckByLoadTime(TimeSpan time) => time == TimeSpan.MaxValue;

        /// <summary>
        /// Перевіряє, чи завантажується текстура зараз. ОПТИМІЗАЦІЯ: пошук у хеш-таблиці за O(1).
        /// </summary>
        public bool IsLoadingByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            lock (_syncLock)
            {
                return _queuedItems.Contains(name) || _loadingNow.Contains(name);
            }
        }

        /// <summary>
        /// Періодичне фонове оновлення черг текстур.
        /// ОПТИМІЗАЦІЯ: мережеві операції виконуються поза блокуванням lock, усуваючи затримки кадру в GUI.
        /// </summary>
        public void Update(SessionClient connect)
        {
            const int CountUpdateInRun = 1;
            const int CountCheckInRun = 100;

            // 1. Очищення застарілих скріншотів без створення нових списків ключів
            var now = DateTime.UtcNow;
            _expiredAgingsBuffer.Clear();

            lock (_syncLock)
            {
                foreach (var kvp in LoadedAgings)
                {
                    if (kvp.Value < now)
                    {
                        _expiredAgingsBuffer.Add(kvp.Key);
                    }
                }

                for (int i = 0; i < _expiredAgingsBuffer.Count; i++)
                {
                    var aging = _expiredAgingsBuffer[i];
                    LoadedAgings.Remove(aging);
                    if (LoadedTextures.TryRemove(aging, out var old))
                    {
                        LoadedOldTextures.TryAdd(aging, old);
                    }
                }
            }

            // 2. Підготовка пакета пакетної перевірки хешів
            _checkNamesBuffer.Clear();
            _checkMfsBuffer.Clear();

            lock (_syncLock)
            {
                // Якщо елементів мало (1-3) і черга завантаження порожня — одразу переводимо на скачування без попереднього запиту
                if (_checkQueue.Count > 0 && _checkQueue.Count <= 3 && _downloadQueue.Count == 0)
                {
                    while (_checkQueue.Count > 0)
                    {
                        _downloadQueue.Enqueue(_checkQueue.Dequeue());
                    }
                }
                else if (_checkQueue.Count > 3)
                {
                    int toCheck = Math.Min(CountCheckInRun, _checkQueue.Count);
                    for (int i = 0; i < toCheck; i++)
                    {
                        var name = _checkQueue.Dequeue();
                        LoadedOldTextures.TryGetValue(name, out var oldTexture);
                        var hash = oldTexture?.Hash ?? CacheResource.GetHash(name);

                        var mfs = GetModelFileSharing(name, hash);
                        if (mfs == null)
                        {
                            _queuedItems.Remove(name);
                            continue;
                        }

                        _checkNamesBuffer.Add(name);
                        _checkMfsBuffer.Add(mfs);
                    }
                }
            }

            // 3. Виконання мережевого запиту перевірки хешів БЕЗ утримання блокування
            if (_checkMfsBuffer.Count > 0)
            {
                List<ModelFileSharing> checkResult = null;
                try
                {
                    checkResult = connect.FileSharingDownloadOnlyCheck(_checkMfsBuffer);
                }
                catch (Exception ex)
                {
                    Loger.Log("GeneralTexture FileSharingDownloadOnlyCheck Exception: " + ex.Message, Loger.LogLevel.WARNING);
                }

                if (checkResult != null)
                {
                    for (int i = 0; i < _checkNamesBuffer.Count; i++)
                    {
                        var name = _checkNamesBuffer[i];
                        var itemMfs = _checkMfsBuffer[i];
                        var res = (i < checkResult.Count) ? checkResult[i] : null;

                        // Якщо файлу на сервері немає або хеш ідентичний локальному кешу
                        if (res?.Hash == null || itemMfs.Hash == res.Hash)
                        {
                            if (!LoadedOldTextures.TryGetValue(name, out var texture))
                            {
                                if (res?.Hash == null)
                                {
                                    texture = new TextureContainer(Null);
                                }
                                else
                                {
                                    texture = new TextureContainer
                                    {
                                        Hash = itemMfs.Hash,
                                        Data = CacheResource.GetData(name)
                                    };
                                }
                            }

                            lock (_syncLock)
                            {
                                _queuedItems.Remove(name);
                            }

                            SetLoadedTextures(name, texture);
                        }
                        else
                        {
                            // Хеш відрізняється — переводимо в чергу безпосереднього завантаження
                            lock (_syncLock)
                            {
                                _downloadQueue.Enqueue(name);
                            }
                        }
                    }
                }
                else
                {
                    // У разі збою мережі повертаємо елементи назад у чергу перевірки
                    lock (_syncLock)
                    {
                        for (int i = 0; i < _checkNamesBuffer.Count; i++)
                        {
                            _checkQueue.Enqueue(_checkNamesBuffer[i]);
                        }
                    }
                }
            }

            // 4. Безпосереднє завантаження файлу (по CountUpdateInRun за тік)
            for (int i = 0; i < CountUpdateInRun; i++)
            {
                string downloadName = null;
                TextureContainer oldTexture = null;
                ModelFileSharing mfs = null;

                lock (_syncLock)
                {
                    while (_downloadQueue.Count > 0)
                    {
                        var candidate = _downloadQueue.Dequeue();
                        if (!_queuedItems.Contains(candidate)) continue;

                        downloadName = candidate;
                        _loadingNow.Add(downloadName);
                        _queuedItems.Remove(downloadName);
                        break;
                    }
                }

                if (downloadName == null) break;

                try
                {
                    if (!LoadedOldTextures.TryRemove(downloadName, out oldTexture))
                    {
                        oldTexture = new TextureContainer(Null);
                    }

                    mfs = GetModelFileSharing(downloadName, oldTexture.Hash);
                    if (mfs == null) continue;

                    // Скачування даних через мережу БЕЗ утримання блокування
                    ModelFileSharing packet = null;
                    try
                    {
                        packet = connect.FileSharingDownload(mfs);
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("GeneralTexture FileSharingDownload Exception: " + ex.Message, Loger.LogLevel.WARNING);
                    }

                    TextureContainer texture;
                    if (packet?.Data != null && packet.Data.Length > 0)
                    {
                        texture = new TextureContainer
                        {
                            Hash = packet.Hash,
                            Data = packet.Data
                        };
                    }
                    else
                    {
                        if (packet?.Hash == null || oldTexture.Hash != packet?.Hash)
                        {
                            Loger.Log("Client GeneralTexture Error load: " + downloadName + " " + oldTexture.Hash + "!=" + packet?.Hash, Loger.LogLevel.ERROR);
                        }
                        texture = oldTexture;
                    }

                    SetLoadedTextures(downloadName, texture);
                }
                finally
                {
                    lock (_syncLock)
                    {
                        _loadingNow.Remove(downloadName);
                    }
                }
            }
        }

        private void SetLoadedTextures(string name, TextureContainer texture)
        {
            texture.LoadTime = DateTime.UtcNow;

            if (name.StartsWith("cs_"))
            {
                lock (_syncLock)
                {
                    LoadedAgings[name] = DateTime.UtcNow.AddSeconds(UpdateSecondColonyScreen);
                }
            }

            LoadedTextures[name] = texture;

            if (texture.Data != null)
            {
                CacheResource.SetData(name, texture.Data);
            }
        }

        private ModelFileSharing GetModelFileSharing(string name, string hash)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 4) return null;
            var sendName = name.Substring(3);

            FileSharingCategory category;
            if (name.StartsWith("pl_")) category = FileSharingCategory.PlayerIcon;
            else if (name.StartsWith("cs_")) category = FileSharingCategory.ColonyScreen;
            else return null;

            return new ModelFileSharing
            {
                Category = category,
                Name = sendName,
                Hash = hash
            };
        }
    }
}