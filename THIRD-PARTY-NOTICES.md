# Third-party notices

MikroLab — самостоятельное приложение, но его ядро выросло из открытого учебного
проекта. Ниже перечислены работы, которые повлияли на код и дизайн, и условия их
использования.

## ZeeWanderer/Emulator

- **Проект:** Emulator — эмулятор КР580ВМ80А (Intel 8080) для студентов ВНТУ
- **Автор:** ZeeWanderer (maximilian578, «max»)
- **Ссылка:** https://github.com/ZeeWanderer/Emulator
- **Лицензия:** GNU General Public License v3.0
- **Что использовано:** исходная модель процессора и устройств, структура машины,
  отдельные алгоритмы и поведение лабораторного стенда. В MikroLab этот код
  переработан, разделён на headless-ядро и интерфейс, дополнен ассемблером,
  расширенным набором команд, новыми устройствами и лабораторными работами.
- **Условия:** производные работы распространяются под GPLv3. Полный текст
  лицензии находится в файле [LICENSE](LICENSE).

Проект предоставляется «как есть», без каких-либо гарантий, явных или
подразумеваемых, включая гарантии товарной пригодности и пригодности для
конкретной цели.

## WhoSowSee/KR580

- **Проект:** KR580 — современный нативный эмулятор КР580 на Rust
- **Автор:** WhoSowSee
- **Ссылка:** https://github.com/WhoSowSee/KR580
- **Лицензия:** MIT License
- **Что использовано:** проект изучен как пример современного интерфейса
  эмулятора и организации наблюдаемого выполнения. Исходный код KR580 в MikroLab
  не копировался; совпадающие идеи (редактирование состояния, пошаговое
  выполнение, наглядные панели) реализованы самостоятельно на C#/WinForms и
  C#/Avalonia.

## .NET и Windows Forms

- **Проект:** .NET Runtime и Windows Forms
- **Автор:** Microsoft и участники .NET
- **Ссылка:** https://github.com/dotnet/runtime
- **Лицензия:** MIT License
- **Что использовано:** платформа выполнения, WinForms, System.Drawing и
  стандартная библиотека.

## Avalonia UI

- **Проект:** Avalonia — кроссплатформенный UI-фреймворк для .NET
- **Автор:** AvaloniaUI OÜ и участники проекта
- **Ссылка:** https://github.com/AvaloniaUI/Avalonia
- **Лицензия:** MIT License
- **Что использовано:** пакеты `Avalonia`, `Avalonia.Desktop` и
  `Avalonia.Themes.Fluent` версии 12.1.4, а также их транзитивные компоненты
  (SkiaSharp, HarfBuzzSharp, Tmds.DBus.Protocol, MicroCom) для
  кроссплатформенной студии. Пакеты распространяются под MIT License; ANGLE
  для Windows — под BSD-3-Clause.
- **Условия:** текст MIT License, общий для Avalonia и перечисленных
  компонентов:

```text
MIT License

Copyright (c) AvaloniaUI OÜ and contributors
Copyright (c) Microsoft Corporation
Copyright (c) Tom Deseyn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Документация и скриншоты

Скриншоты в `docs/screens` созданы самим приложением в режиме `--capture` и
распространяются вместе с проектом под GPLv3. Шрифты, используемые интерфейсом,
поставляются операционной системой (Windows, Linux или macOS).

## Контакт

Вопросы об авторстве и лицензировании: issue в репозитории MikroLab.
