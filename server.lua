-- No server logic needed for state bags, but manifest requires a file if defined
AddEventHandler('onResourceStart', function(resourceName)
    if GetCurrentResourceName() ~= resourceName then return end
    print("^2[CStancer]^7 Active.")
end)
