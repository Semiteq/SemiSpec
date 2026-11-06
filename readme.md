# SemiSpec

[![CI](https://github.com/Semiteq/SemiSpec/actions/workflows/ci.yml/badge.svg)](https://github.com/Semiteq/SemiSpec/actions/workflows/ci.yml)
![C#](https://img.shields.io/badge/C%23-.NET-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows-informational)

<div align="center">
    <img src=./logo.png width=300 />
</div>

SemiSpec показывает спектр с платы спектрометров ATP-2000 и ATP-8000 и тренд интенсивности на длине волны,
которую задаёт SCADA. Программа читает плату через COM-порт и хранит спектры за последние 24 часа.

<div align="center">
    <img src="./docs/img/axis-flyout.png" width="800" />
    <p><em>Главное окно: спектр, значения платы, тренд, обзор истории и панель интервала времени</em></p>
</div>

## Возможности

- Спектр с окном измерения ±`delta` и значения платы: амплитуда, сигнал/шум, добротность.
- Тренд интенсивности за 24 часа с разрывами на потерянных спектрах и полосой ошибок измерения. Полоса обзора под
  трендом показывает всю историю.
- Щелчок по тренду открывает спектр этого момента.
- Щелчок по оси открывает панель шкалы: границы, логарифмическая шкала, отсчёт от нуля, интервал времени.
- Строка состояния и панель сообщений называют неполадку связи и что сделать.

<div align="center">
    <img src="./docs/img/past-moment.png" width="800" />
    <p><em>Спектр прошлого момента</em></p>
</div>

<div align="center">
    <img src="./docs/img/board-not-responding.png" width="800" />
    <p><em>Плата не отвечает</em></p>
</div>

## Требования

| Компонент            | Требование                              |
| -------------------- | --------------------------------------- |
| Операционная система | Windows 10 или Windows 11, 64-разрядная |

## Установка

Установщик `SemiSpecInstaller.exe` находится на странице
[Releases](https://github.com/Semiteq/SemiSpec/releases/latest).

Журнал программы лежит в папке `%LOCALAPPDATA%\SemiSpec\Logs`.
