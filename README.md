# MinecraftLanguageTranslator

## Overview

An application that uses ChatGPT to read and translate language files of Java edition Minecraft mods and add them to the MOD.

## Basic Configuration

  - Framework: net8.0
  - Application Format: Console Application
  - Translation Mechanism: CHatGPT (version specified)

## Usage

1. Build the application
1. Configure the config file(`./config.ini`)<br/>
  Mainly the source of translation, and the language setting after translation (Reference: https://minecraft.fandom.com/wiki/Language)
1. Place the mods directory in the application directory
1. Run the application
1. Check the output log (`./logs`) and if it has successfully completed until the end, the translation is complete (You may need to run it several times for now)
1. Move the `./mods` directory to the main body
1. Done

## About PR

I created it in a hurry for the education of children, so PR is very welcome.<br/>
If my personality seems okay, I'm the type to merge it quickly.<br/>
Feel free to make corrections.
