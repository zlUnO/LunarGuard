-- Test script for LunarGuard
local function greet(name)
    print("Hello, " .. name .. "!")
end

local function calculate(a, b)
    local sum = a + b
    local product = a * b
    return sum, product
end

local player = {
    name = "Player1",
    health = 100,
    ammo = 30
}

function player:takeDamage(amount)
    self.health = self.health - amount
    if self.health <= 0 then
        print("Player died!")
    end
end

greet("World")
local s, p = calculate(10, 20)
print("Sum:", s, "Product:", p)
player:takeDamage(50)
