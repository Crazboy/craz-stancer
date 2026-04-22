-- CStancer Server Side
RegisterNetEvent("cstancer:reportStance")
AddEventHandler("cstancer:reportStance", function(netId, data)
    if not netId or not data then return end
    
    local entity = NetworkGetEntityFromNetworkId(netId)
    if DoesEntityExist(entity) then
        -- Server sets the state bag to ensure reliable network replication
        Entity(entity).state:set('cstancer:data', data, true)
    end
end)

RegisterNetEvent("cstancer:clearStance")
AddEventHandler("cstancer:clearStance", function(netId)
    if not netId then return end
    local entity = NetworkGetEntityFromNetworkId(netId)
    if DoesEntityExist(entity) then
        Entity(entity).state:set('cstancer:data', nil, true)
    end
end)

AddEventHandler('onResourceStart', function(resourceName)
    if GetCurrentResourceName() ~= resourceName then return end
    print("^2[CStancer]^7 Server sync active.")
end)
