# SkySubscriptions
Subscription managing and sending for skyblock.  
Only one instance of this service should ever be active. 

Configuration can be found in `appsettings.json`.
You can overwrite it via Enviroment variables. 

> **Note**: the keys represent the JSON path of a value and `:` has to be replaced with `__`. eg `TOPICS:FLIP` becomes `TOPICS__FLIP`

