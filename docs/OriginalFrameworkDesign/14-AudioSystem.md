# 音频系统

## 架构

```
ERB 脚本: PLAYSOUND / PLAYBGM / STOPSOUND
  ↓
EmueraConsole (指令执行)
  ↓
uEmuera.Window.MainWindow (桥接层)
  ↓
EmueraContent (Godot 音频节点管理)
  ├── bgmPlayer (AudioStreamPlayer) — BGM
  └── soundPlayers[] (AudioStreamPlayer[]) — 音效通道
```

## 音频播放器管理

```csharp
// EmueraContent.cs
AudioStreamPlayer bgmPlayer;                    // 背景音乐播放器
List<AudioStreamPlayer> soundPlayers;           // 音效通道池
List<int> soundRepeatRemaining;                 // 各通道剩余循环次数
float soundVolume = 1.0f;                       // 音效音量
float bgmVolume = 1.0f;                         // BGM 音量
```

## ERB 音频指令

| 指令 | 功能 |
|------|------|
| `PLAYSOUND "file"` | 播放音效 |
| `PLAYSOUND "file", channel` | 指定通道播放 |
| `PLAYBGM "file"` | 播放背景音乐 (循环) |
| `STOPSOUND` | 停止所有音效 |
| `STOPSOUND channel` | 停止指定通道 |
| `STOPBGM` | 停止背景音乐 |
| `SOUNDVOLUME channel, vol` | 设置通道音量 |
| `BGMVOLUME vol` | 设置 BGM 音量 |

## 音频文件查找

```
游戏目录/
├── sound/          # 音效文件目录
│   ├── *.wav
│   ├── *.ogg
│   └── *.mp3
└── music/          # BGM 目录 (部分游戏)
```

支持格式（通过 Godot AudioStream）：
- WAV (AudioStreamWAV)
- OGG Vorbis (AudioStreamOggVorbis)
- MP3 (AudioStreamMP3)

## Android 生命周期与音频

```csharp
// 进入后台时暂停音频
void SetApplicationPaused(bool paused)
{
    if (paused)
    {
        bgmPausedBeforeApplicationPause = !bgmPlayer.Playing || bgmPlayer.StreamPaused;
        if (bgmPlayer.Playing) bgmPlayer.StreamPaused = true;
        
        for (int i = 0; i < soundPlayers.Count; i++)
        {
            soundPausedBeforeApplicationPause[i] = !soundPlayers[i].Playing;
            if (soundPlayers[i].Playing) soundPlayers[i].StreamPaused = true;
        }
    }
    else
    {
        // 恢复之前的播放状态
        if (!bgmPausedBeforeApplicationPause) bgmPlayer.StreamPaused = false;
        for (int i = 0; i < soundPlayers.Count; i++)
        {
            if (!soundPausedBeforeApplicationPause[i])
                soundPlayers[i].StreamPaused = false;
        }
    }
}
```

## 音频加载

音频文件从游戏目录运行时加载（非 Godot res://），需要：

1. 读取原始文件字节
2. 根据格式创建对应 AudioStream
3. 设置到 AudioStreamPlayer
4. 播放

```csharp
AudioStream LoadAudioFromFile(string path)
{
    byte[] data = File.ReadAllBytes(path);
    string ext = Path.GetExtension(path).ToLower();
    
    switch (ext)
    {
        case ".ogg":
            return AudioStreamOggVorbis.LoadFromBuffer(data);
        case ".mp3":
            var mp3 = new AudioStreamMP3();
            mp3.Data = data;
            return mp3;
        case ".wav":
            // WAV 需要解析头部
            return ParseWavToStream(data);
    }
}
```

## 通道管理

音效通道按需扩展，脚本可指定通道号：

```csharp
void EnsureChannelExists(int channel)
{
    while (soundPlayers.Count <= channel)
    {
        var player = new AudioStreamPlayer();
        AddChild(player);
        soundPlayers.Add(player);
        soundRepeatRemaining.Add(0);
    }
}
```
