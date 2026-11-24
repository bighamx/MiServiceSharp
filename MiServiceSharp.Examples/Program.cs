using MiServiceSharp;
using MiServiceSharp.Auth;
using MiServiceSharp.Examples;
using MiServiceSharp.Storage;

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

// 账号可通过 MI_USER、MI_PASS 配置，已有凭证时直接加载项目目录中的 Token。
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
var account = new MiAccountClient(http, new MiAccountOptions
{
    Username = Environment.GetEnvironmentVariable("MI_USER") ?? string.Empty,
    Password = Environment.GetEnvironmentVariable("MI_PASS") ?? string.Empty
}, new FileMiTokenStore(FileMiTokenStore.GetDefaultPath()));

try
{
    await account.InitializeAsync(stopping.Token);
    var client = new MiServiceFacade(account);

    var devices = await DeviceExamples.ListAsync(client, stopping.Token);

    // 灯：开关、亮度和色温，对应 LightExamples 中的控制和 toggle 动作。
    var livingRoomLight = devices.Single(device => device.Name == "客厅灯");
    await DeviceExamples.DescribeAsync(livingRoomLight, stopping.Token);
    await LightExamples.ReadStateAsync(livingRoomLight, stopping.Token);

    // 插座与插排：相同 switch 服务，插排还具有功率读取和 toggle 动作。
    var plug = devices.Single(device => device.Name == "智能插座");
    var powerStrip = devices.Single(device => device.Name == "电脑插排");
    await PlugExamples.ReadStateAsync(plug, stopping.Token);
    await PlugExamples.ReadPowerStripAsync(powerStrip, stopping.Token);

    // 空调：跨空调、风扇和环境服务读取模式、目标温度、室温和风速。
    var airConditioner = devices.Single(device => device.Model == "xiaomi.aircondition.mc6");
    await AirConditionerExamples.ReadStateAsync(airConditioner, stopping.Token);

    // 净化器与新风机：空气质量、运行模式和滤芯寿命。
    var purifier = devices.Single(device => device.Name == "空净");
    var freshAir = devices.Single(device => device.Name == "米家新风机");
    await AirPurifierExamples.ReadStateAsync(purifier, stopping.Token);
    await FreshAirExamples.ReadStateAsync(freshAir, stopping.Token);

    // 摄像机：夜视、移动追踪、存储卡，以及示例文件中的人形移动事件订阅。
    var camera = devices.Single(device => device.Model == "chuangmi.camera.069a01");
    await CameraExamples.ReadStateAsync(camera, stopping.Token);

    // 温湿度、门磁与燃气传感器：各自具有不同服务和属性。
    var environmentSensor = devices.Single(device => device.Name == "小米温湿度传感器");
    var doorSensor = devices.Single(device => device.Name == "门窗传感器");
    var gasSensor = devices.Single(device => device.Name == "小米天然气卫士");
    await TemperatureHumidityExamples.ReadStateAsync(environmentSensor, stopping.Token);
    await DoorSensorExamples.ReadStateAsync(doorSensor, stopping.Token);
    await GasSensorExamples.ReadStateAsync(gasSensor, stopping.Token);

    // 网关：布防状态及 ParentId 关联的 Zigbee 子设备。
    var gateway = devices.Single(device => device.Name == "小米多功能网关");
    await GatewayExamples.ReadStateAsync(gateway, devices, stopping.Token);

    // 热水器与油烟机：温度、工作状态和风速。
    var waterHeater = devices.Single(device => device.Name == "热水器");
    var rangeHood = devices.Single(device => device.Name == "抽油烟机");
    await WaterHeaterExamples.ReadStateAsync(waterHeater, stopping.Token);
    await RangeHoodExamples.ReadStateAsync(rangeHood, stopping.Token);

    // 扫地机器人：状态、电量，控制方法中还展示清扫、暂停和回充动作。
    var robot = devices.Single(device => device.Name == "扫地机器人");
    if (robot.Info.IsOnline)
    {
        await RobotVacuumExamples.ReadStateAsync(robot, stopping.Token);
    }
    else
    {
        Console.WriteLine($"{robot.Name} 当前离线，恢复在线后可以查询状态和电量。");
    }

    // 音箱：MiNA 状态，另有音量、播报与 MIoT 动作示例。
    await SpeakerExamples.StatusAsync(client, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    Console.WriteLine("示例已取消。");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

return 0;
