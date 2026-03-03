-- slomo.lua
-- Starts video at 1.0x, eases down to 0.6x, and eases back to 1.0x over a full cycle

local min_speed = 0.6
local max_speed = 1.0
local half_cycle_duration = 2.0 -- Time to go from 1.0 to 0.6
local full_cycle_duration = half_cycle_duration * 2
local fps = 30
local dt = 1.0 / fps

local timer = nil

function apply_slomo()
    if timer ~= nil then
        timer:kill()
        timer = nil
    end

    -- Reset to max speed initially
    mp.set_property("speed", max_speed)
    
    local steps = full_cycle_duration * fps
    local current_step = 0
    
    timer = mp.add_periodic_timer(dt, function()
        current_step = current_step + 1
        local t = current_step / steps -- Normalized time 0.0 to 1.0
        
        if t > 1 then t = 1 end
        
        -- Cosine Wave Interpolation
        -- At t=0, cos(0)=1 -> max_speed
        -- At t=0.5, cos(pi)=-1 -> min_speed
        -- At t=1.0, cos(2pi)=1 -> max_speed
        
        local midpoint = (max_speed + min_speed) / 2
        local amplitude = (max_speed - min_speed) / 2
        
        local new_speed = midpoint + amplitude * math.cos(t * 2 * math.pi)
        
        mp.set_property("speed", new_speed)
        
        if current_step >= steps then
            mp.set_property("speed", max_speed) -- Ensure clean finish
            timer:kill()
            timer = nil
        end
    end)
end

-- Ensure speed is reset when a new file starts
mp.register_event("start-file", function() mp.set_property("speed", max_speed) end)
-- triggers on start and on loop
mp.register_event("playback-restart", apply_slomo)
