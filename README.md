# AliyunTrafficCheck
阿里云cdt流量检测,超过配置流量则关机,下月初开机.

# 食用方法
### win 

 下载Release中的AliyunTrafficCheck_win64.zip

 解压后修改appsetting.json中的配置,默认只需要填写"AccessKeyId" ,"AccessKeySecret"

### linux(arm,amd)

 下载对应版本 `tar zxvf` 进入文件夹 `chmod +x AliyunTrafficCheck` 后`./AliyunTrafficCheck`

### docker

构建镜像:
```bash
docker build -t aliyun-traffic-check .
```

运行容器:
```bash
docker run -d --name aliyun-traffic-check \
    -e Credentials__AccessKeyId="your_access_key_id" \
    -e Credentials__AccessKeySecret="your_access_key_secret" \
    -e InstanceId="" \
    -e RegionId="cn-hongkong" \
    -e MaxTraffic="180" \
    -e WeChatWebhookUrl="your_wechat_webhook_url" \
    aliyun-traffic-check
```

### docker-compose

使用 docker-compose 部署（推荐方式）:
```bash
docker-compose up -d
```

在使用前请先修改 docker-compose.yml 文件中的环境变量配置。

## 微信群推送配置

如需在实例开关机时接收微信通知，可以配置企业微信机器人：

1. 在企业微信群中添加机器人，获取 webhook 地址
2. 将 webhook 地址配置到环境变量 `WeChat__WebhookUrl` 中
3. 支持在 appsettings.Development.json 中配置:
   ```json
   {
     "WeChatWebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=your_key"
   }
   ```

## TODO
1. linux安装,卸载脚本.
2. 测试Docker部署
