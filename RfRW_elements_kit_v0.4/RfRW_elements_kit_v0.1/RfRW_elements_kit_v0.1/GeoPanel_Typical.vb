Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode
Imports Shape = Autodesk.Civil.DatabaseServices.Shape
Imports System.Math

Public Class GeoPanel_Typical
    Inherits SATemplate
    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: GeoPanel
    '
    '   Description: Creates a simple cross-sectional representation of a reinforcement soil wall composed of an array of geogrids.Attachment origin
    '                is at bottom.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetElevation             Profile    Yes       May be used to set height
    '                TargetOffset              Alignment    Yes       May be used to set width
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                FaceAngle              double      no          0                degrees of face element slope
    '                Side                   long        no          Right            specifies side to place SA on
    '                Width                  double      no          3.0              width of geogrids
    '                verticalStep           double      no          0.5              step of geogrid layer in vertical
    '                horizontalStep         double      no          0.0              step of layer in horizontal
    '                DrenageOffset          double      yes         0.3              use if target false
    '                DrenageElevation       long        yes          1               use if target false
    '                AssemblyName           string      no           1               use for point\link\shape names at some regions
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None
    Private Const FaceAngleDefault = 0.01
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const WidthDefault = 3.0
    Private Const vStepDefault = 0.5
    Private Const hStepDefault = 0.0
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    Private Const dSubAsNameDefault = "Участок"
    Private Const dBlockLength = 2.7
    Private Const dBlockHeight = 0.5
    Private Const dDrenWidth = 0.3
    Private Const dSubHeight = 0.3
    Private Const dGravelSlopeDefault = 1.5

    Private Shared _blocksCount As Integer = 0 'необходима для хранения значения на протяжении перестроения всего коридора
    'Добавляем информацию о входных параметрах
    Protected Overrides Sub GetInputParametersImplement(corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)
        'создаем контейнеры для хранения инфы и присваиваем им значания соответствующих позиций из corridorState
        'for long
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'for double
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble
        'for string
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        'добавляем в эти контейнеры входные параметры которые будем использовать
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsDouble.Add("Наклон лицевой грани", FaceAngleDefault)
        paramsDouble.Add("Длина георешеток", WidthDefault)
        paramsDouble.Add("Шаг георешеток", vStepDefault)
        paramsDouble.Add("Отскок слоя", hStepDefault)
        paramsDouble.Add("Заложение дренажных призм", dGravelSlopeDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        'параметры георешеток (тип и кол-во слоев)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Ширина дренажа минимальная", dDrenWidth)
        paramsDouble.Add("Ширина дренажа верхнего слоя", dDrenWidth)
        paramsDouble.Add("Высота щебеночного основания", dSubHeight)
    End Sub
    'при необходимости добавляем выходные параметры
    Protected Overrides Sub GetOutputParametersImplement(corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)
    End Sub
    'добавляем логические переменные
    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Design_Prof", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Design_Axis", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"

        ParamLong = paramsLong.Add("Panels_Top", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных панелей"
    End Sub
    'создание логики построения и отрисовки элемента конструкции
    Protected Overrides Sub DrawImplement(corridorState As CorridorState)
        'объявим транзакцию
        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager
        'собираем ранее введеные параметры из corridorState
        'целевой профиль
        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget
        'целевой отступ (трасса)
        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
        'коллекции переменных
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        '-----------------------------------------
#Region "создаем переменные, которые будут принимать значения от введенных параметров, для использования в нашем коде"
        'определяем сторону в которую строится конструкция
        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '------------------------------
        'переменная, которую будем использовать для отзеркаливания 
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '------------------------------
        Dim gridWidth As Double
        Try
            gridWidth = paramsDouble.Value("Длина георешеток")
        Catch
            gridWidth = WidthDefault
        End Try
        '------------------------------
        Dim faceAngle As Double
        Try
            faceAngle = paramsDouble.Value("Наклон лицевой грани")
        Catch
            faceAngle = FaceAngleDefault
        End Try
        '------------------------------
        Dim gStep As Double
        Try
            gStep = paramsDouble.Value("Шаг георешеток") '(он же высота слоя)
        Catch
            gStep = vStepDefault
        End Try
        '------------------------------
        Dim hStep As Double
        Try
            hStep = paramsDouble.Value("Отскок слоя")
        Catch
            hStep = hStepDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        Dim RE580_count As Long
        Try
            RE580_count = paramsLong.Value("Кол-во RE580")
        Catch
            RE580_count = dRE580_countDefault
        End Try
        '----------------------------------------
        Dim RE570_count As Long
        Try
            RE570_count = paramsLong.Value("Кол-во RE570")
        Catch
            RE570_count = dRE570_countDefault
        End Try
        '----------------------------------------
        Dim RE560_count As Long
        Try
            RE560_count = paramsLong.Value("Кол-во RE560")
        Catch
            RE560_count = dRE560_countDefault
        End Try
        '----------------------------------------
        Dim RE540_count As Long
        Try
            RE540_count = paramsLong.Value("Кол-во RE540")
        Catch
            RE540_count = dRE540_countDefault
        End Try
        '----------------------------------------
        Dim RE520_count As Long
        Try
            RE520_count = paramsLong.Value("Кол-во RE520")
        Catch
            RE520_count = dRE520_countDefault
        End Try
        '----------------------------------------
        Dim oSubAsName As String
        Try
            oSubAsName = paramsString.Value("Имя участка")
        Catch
            oSubAsName = dSubAsNameDefault
        End Try
        '----------------------------------------
        Dim wDren As Double
        Try
            wDren = paramsDouble.Value("Ширина дренажа минимальная")
        Catch
            wDren = hStepDefault
        End Try
        '------------------------------
        Dim wDrenTop As Double
        Try
            wDrenTop = paramsDouble.Value("Ширина дренажа верхнего слоя")
        Catch
            wDrenTop = hStepDefault
        End Try
        '------------------------------
        Dim dL As Double
        Try
            dL = paramsDouble.Value("BlockLength")
        Catch
            dL = dBlockLength
        End Try
        '----------------------------------------
        Dim subHeight As Double
        Try
            subHeight = paramsDouble.Value("Высота щебеночного основания")
        Catch
            subHeight = dSubHeight
        End Try
        '----------------------------------------
        Dim drenSlope As Double
        Try
            drenSlope = 1 / paramsDouble.Value("Заложение дренажных призм")
        Catch
            drenSlope = dGravelSlopeDefault
        End Try
#End Region
        ' проверка введенных пользователем значений и создание их ограничений
        ' например минимум по длине 
        If gridWidth < 3 Then
            Utilities.RecordError(corridorState, CorridorError.ValueTooSmall, "Длина георешеток", "Geogrid")
            gridWidth = WidthDefault
        End If
        ' или ограничение шага геоармирования
        If gStep <= 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanOrEqualToZero, "Шаг георешеток", "Geogrid")
            gStep = vStepDefault
        End If
#Region "находим трассу и ориджин для рассматриваемого сечения"
        Dim oOrigin As New PointInMem '(это точка в рассматриваемом сечении, относительно которой строится текущий элемент констр. при переносе элемента, ориджин помогает не потеряться в сечении)
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)
        If corridorState.Mode = CorridorMode.Design Then 'при построении элементов внутри коридора
            'найдем цели для построения элемента конструкции
            ' высоту конструкции
            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Design_Prof")
            Catch
                elevationTarget = Nothing
            End Try
            Dim hasWallElevationProfile As Boolean
            hasWallElevationProfile = False
            Dim wallHeight As Double
            If Not elevationTarget Is Nothing Then
                Try
                    wallHeight = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallElevationProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Design_Prof", "RetainWallVertical")
                End Try
            End If
            Dim panelsElevTarget As SlopeElevationTarget
            Try
                panelsElevTarget = oParamsElevationTarget.Value("Panels_Top")
            Catch
                panelsElevTarget = Nothing
            End Try

            Dim hasWallBlocksProfile As Boolean
            hasWallBlocksProfile = False
            Dim blocksHeight As Double

            If Not panelsElevTarget Is Nothing Then
                'получим высоту по профилю
                Try
                    blocksHeight = panelsElevTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallBlocksProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Panels_Top", "RetainWallVertical")
                End Try
                'сечение по заданному профилю высоты блоков+проектному
            End If
            ' и отступ грунта засыпки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Design_Axis")
            Catch
                offsetTarget = Nothing
            End Try
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double = gridWidth + 1

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    soilOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Design_Axis", "RetainWallHorizontal")
                End Try
            End If
            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / gStep, Integer)

            Else 'в случае отсутствия профиля для определения высоты облицовки (для первого прохода например)
                'в начале каждого региона(области) добавляем сечения в пикетах шага облицовочного блока TW1
                If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, gStep, dL, elevationTarget) 'доп сечения для облицовки
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = gStep * 1000
                    _blocksCount = wallHeight * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 3
                        If isStep(tm, corridorState, firstTop) Then
                            _blocksCount -= 1
                        End If
                        firstTop += 0.001
                    End While
                End If

                'условие для переопределения высоты облицовки
                If isStep(tm, corridorState, corridorState.CurrentStation) And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation + 0.001 Then
                    'вспомогательные вектора до и после скачка для оценки направления проектного профиля
                    Dim beforeStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation - 0.01)
                    Dim afterStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation + 0.01)
                    'сравниваем текущую высоту по блокам и высоту луча(общую высоту стенки)
                    If beforeStep < afterStep Then
                        Dim dif As Integer = (afterStep - oOrigin.Elevation - _blocksCount * gStep) * 1000 \ (gStep * 1000)
                        _blocksCount += dif
                    ElseIf beforeStep > afterStep Then
                        Dim dif As Integer = (_blocksCount * gStep - afterStep - oOrigin.Elevation) * 1000 \ (gStep * 1000)
                        _blocksCount -= (dif + 1)
                    Else
                        Throw New Exception("что-то неладное")
                    End If

                End If
                rows = _blocksCount
            End If
            Dim zeroPoint As New PointInMem With {
                .Offset = 0,
                .Elevation = 0
                }

#End Region

            '---------------------------
            'отсюда начинается обработка введеных значений и построение конструкции
            '---------------------------
            wallCreate(corridorState, wallHeight, soilOffset, gridWidth, gStep, hStep, wDren, wDrenTop, subHeight, geotextileOverlap, faceAngle, flip, oSubAsName, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count, rows, gStep, drenSlope, hasWallOffsetTarget, zeroPoint)
        Else 'при построении элементов на виде конструкции
            Dim wallHeight As Double
            Dim soilOffset As Double = gridWidth + 1.0
            Dim totalCount = RE520_count + RE540_count + RE560_count + RE570_count + RE580_count
            Dim hasWallOffsetTarget = False
            Dim lastHeight As Double = 0.4
            If totalCount = 0 Then
                wallHeight = 2.0 + lastHeight
            Else
                wallHeight = totalCount * gStep + lastHeight
            End If
            Dim rows As Integer = wallHeight * 1000 \ gStep * 1000
            wallCreate(corridorState, wallHeight, soilOffset, gridWidth, gStep, hStep, wDren, wDrenTop, subHeight, geotextileOverlap, faceAngle, flip, oSubAsName, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count, rows, gStep, drenSlope, hasWallOffsetTarget, oOrigin)
        End If

        Dim param As IParam
        param = paramsLong.Add(Utilities.Side, SideDefault)
        param = paramsDouble.Add("Наклон лицевой грани", FaceAngleDefault)
        param = paramsDouble.Add("Длина георешеток", WidthDefault)
        param = paramsDouble.Add("Шаг георешеток", vStepDefault)
        param = paramsDouble.Add("Отскок слоя", hStepDefault)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Ширина дренажа минимальная", dDrenWidth)
        paramsDouble.Add("Ширина дренажа верхнего слоя", dDrenWidth)
        paramsDouble.Add("Высота щебеночного основания", dSubHeight)
        paramsDouble.Add("Заложение дренажных призм", dGravelSlopeDefault)
    End Sub
    'метод для создания точек вставки слоев
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal wallWidth As Double,
                           ByVal gridWidth As Double,
                           ByVal verticalStep As Double,
                           ByVal horizontalStep As Double,
                           ByVal drenageWidth As Double,
                           ByVal drenageTopWidth As Double,
                           ByVal baseHeight As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal faceAngle As Double,
                           ByVal flipValue As Double,
                           ByVal subAsName As String,
                           ByVal RE520Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE580Count As Long,
                           ByVal blocksCount As Integer,
                           ByVal blockHeight As Double,
                           ByVal drenSlope As Double,
                           ByVal hasTargetOffset As Boolean,
                           ByVal startInputPoint As PointInMem
                           )
        'для первой отладки создадим видимые точки
        'Dim testPoints As PointCollection
        'testPoints = corridorState.Points
        'Dim testPoint As Point

        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem

        Dim elevatP As Double = 0.0 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = 0.0
        Dim dX As Double = (horizontalStep + horizontalStep * Math.Tan(faceAngle * Math.PI / 180)) * flipValue 'отступ для каждого вышележащего ряда (в метрах) 
        'определим кол-во слоев
        Dim layers As Integer = blocksCount
        'layers = wallHeight * 1000 \ verticalStep * 1000
        'определим остаток сверху
        Dim reminder As Double
        reminder = wallHeight - verticalStep * layers
        Dim isFirstlayer As Boolean = True
        Dim isLastlayer As Boolean = False

        Dim geotextileName As String = "Геотекстиль"
        Dim soilName As String = "Дренирующий грунт"
        Dim drenageName As String = "Щебень дренажной призмы"
        Dim gridNameRE520 As String = "Георешетка RE520"
        Dim gridNameRE540 As String = "Георешетка RE540"
        Dim gridNameRE560 As String = "Георешетка RE560"
        Dim gridNameRE570 As String = "Георешетка RE570"
        Dim gridNameRE580 As String = "Георешетка RE580"
        Dim soilNameLast As String = "Дренирующий грунт верхний слой"
        Dim drenageNameLast As String = "Щебень выравнивающей призмы"
        Dim baseName As String = "Щебень основания"
        Dim baseGrid As String = "Георешетка TX"

        createSubbase(corridorState, flipValue, wallWidth, baseHeight, baseName, baseGrid, insertPoint)
        'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
        Dim i As Integer = 1
        Do While layers >= i
            'testPoint = testPoints.Add(offsetP, elevatP, i.ToString())
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            If i = layers Then 'поднимаем флажок, если последний слой
                isLastlayer = True
            End If
            'логика присваивания названия слоя
            gridLayer(corridorState, i, insertPoint, flipValue, gridWidth,
                      gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520,
                      RE580Count, RE570Count, RE560Count, RE540Count, RE520Count)
            'создание слоя засыпки
            createSoilLayer(corridorState, subAsName, drenageName, soilName,
                            geotextileName, wallWidth, verticalStep, drenageWidth,
                            faceAngle, flipValue, geotxtOverlap, hasTargetOffset,
                            isFirstlayer, isLastlayer, drenSlope, insertPoint)
            createPanel(corridorState, verticalStep, faceAngle, flipValue, insertPoint)
            isFirstlayer = False
            elevatP += verticalStep
            offsetP += dX
            i += 1
        Loop
        insertPoint.Offset = offsetP
        insertPoint.Elevation = elevatP
        createTopLayer(corridorState, subAsName, drenageNameLast, soilNameLast, wallWidth, verticalStep, reminder, drenageTopWidth, flipValue, hasTargetOffset, insertPoint)
    End Sub
    'метод для подбора правильного наименования(кода) георешетки 
    Private Sub gridLayer(ByVal corridorstate As CorridorState,
                           ByVal i As Integer,
                           ByVal insertPoint As PointInMem,
                           ByVal flipValue As Double,
                           ByVal gridWidth As Double,
                           ByVal gridNameRE580 As String,
                           ByVal gridNameRE570 As String,
                           ByVal gridNameRE560 As String,
                           ByVal gridNameRE540 As String,
                           ByVal gridNameRE520 As String,
                           ByVal RE580Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE520Count As Long
                           )
        Try 'логика присваивания названия слоя
            If i <= RE580Count Then
                'linkName = subAsName + " " + i.ToString + "_RE580"
                createGeogrid(corridorstate, gridNameRE580, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count < i And i <= RE580Count + RE570Count Then
                'linkName = subAsName + " " + i.ToString + "_RE570"
                createGeogrid(corridorstate, gridNameRE570, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count < i And i <= RE580Count + RE570Count + RE560Count Then
                'linkName = subAsName + " " + i.ToString + "_RE560"
                createGeogrid(corridorstate, gridNameRE560, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count Then
                'linkName = subAsName + " " + i.ToString + "_RE540"
                createGeogrid(corridorstate, gridNameRE540, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count + RE540Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count + RE520Count Then
                'linkName = subAsName + " " + i.ToString + "_RE520"
                createGeogrid(corridorstate, gridNameRE520, gridWidth, flipValue, insertPoint)
            End If
        Catch
            Utilities.RecordWarning(corridorstate, CorridorError.None, "no reinforcement", "ReinfSoilArray")
        End Try
    End Sub
    'метод для создания одной георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
                              ByVal pointToInsert As PointInMem)
        '---------------------------------------------------------
        ' создание точек и связи между ними
        '---------------------------------------------------------
        Dim geogridPoints As PointCollection
        geogridPoints = corridorState.Points

        Dim geogridLinks As LinkCollection
        geogridLinks = corridorState.Links

        Dim gridPoint1 As Point
        Dim gridPoint2 As Point
        Dim gridLink As Link

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset * flipValue, pointToInsert.Elevation, linkName + "1")
        gridPoint2 = geogridPoints.Add((pointToInsert.Offset + geogridWidth) * flipValue, pointToInsert.Elevation, linkName + "2")
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
    'метод создания одного слоя засыпки
    Private Sub createSoilLayer(ByVal corridorState As CorridorState,
                                ByVal subAsName As String,
                                ByVal drenageName As String,
                                ByVal soilName As String,
                                ByVal gtxtName As String,
                                ByVal width As Double,
                                ByVal layerHeight As Double,
                                ByVal stoneTopWidth As Double,
                                ByVal faceAngle As Double,
                                ByVal flipValue As Double,
                                ByVal geotxtOverlap As Double,
                                ByVal hasTargetOffset As Boolean,
                                ByVal isFirstlayer As Boolean,
                                ByVal isLastlayer As Boolean,
                                ByVal stoneSlope As Double,
                                ByVal pointToInsert As PointInMem)
        'объявляем коллекции элементов
        Dim Points As PointCollection
        Points = corridorState.Points
        Dim Links As LinkCollection
        Links = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        'вычисляем вспомогательные параметры
        Dim dOffset = layerHeight * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        'Dim stoneSlope = 1 / 1.5
        Dim gOffset As Double = layerHeight / stoneSlope * flipValue 'gravel offset
        '----------------------
        'Строим слой по точкам
        '----------------------
        Dim Point1 As Point = Points.Add(pointToInsert.Offset, pointToInsert.Elevation, "")
        Dim Point2 As Point
        If isLastlayer Then
            Point2 = Points.Add(Point1.Offset + dOffset, Point1.Elevation + layerHeight, "Верх облицовки")
        Else
            Point2 = Points.Add(Point1.Offset + dOffset, Point1.Elevation + layerHeight, "")
        End If
        Dim Point3 As Point = Points.Add(Point2.Offset + stoneTopWidth * flipValue, Point2.Elevation, "")
        Dim Point4 As Point = Points.Add(Point3.Offset + layerHeight * flipValue / stoneSlope, Point1.Elevation, "")
        Dim Point5 As Point
        Dim Point6 As Point
        If hasTargetOffset Then
            Point5 = Points.Add(width * flipValue, Point1.Elevation, "")
            Point6 = Points.Add(width * flipValue, Point2.Elevation, "")
        Else
            Point5 = Points.Add(Point1.Offset + width * flipValue, Point1.Elevation, "")
            Point6 = Points.Add(Point2.Offset + width * flipValue, Point2.Elevation, "")
        End If

        Dim Link1 As Link = Links.Add(Point1, Point2, drenageName)
        Dim Link2 As Link = Links.Add(Point2, Point3, drenageName)
        Dim Link3 As Link = Links.Add(Point3, Point4, drenageName)
        Dim Link4 As Link = Links.Add(Point4, Point1, drenageName)
        Dim Link5 As Link = Links.Add(Point4, Point3, "")
        Dim Link6 As Link = Links.Add(Point3, Point6, "")
        Dim Link7 As Link = Links.Add(Point6, Point5, soilName)
        Dim Link8 As Link = Links.Add(Point5, Point4, "")

        Dim gLinks As Link() = {Link1, Link2, Link3, Link4}
        Dim drenShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(gLinks, drenageName)
        Dim sLinks As Link() = {Link5, Link6, Link7, Link8}
        Dim soilShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(sLinks, soilName)
        '------------
        'Геотекстиль
        '------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        If isFirstlayer Then
            geotextilePoint1 = geotxtPoints.Add(Point1.Offset + width * flipValue, Point1.Elevation, "")
        Else
            geotextilePoint1 = geotxtPoints.Add(Point4.Offset + gtxtOffset, Point4.Elevation, "")
        End If
        geotextilePoint2 = geotxtPoints.Add(Point4.Offset, Point4.Elevation, "")
        geotextilePoint3 = geotxtPoints.Add(Point3.Offset, Point3.Elevation, "")
        geotextilePoint4 = geotxtPoints.Add(Point3.Offset + gtxtOffset + gOffset + dOffset, Point3.Elevation, "")

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

    End Sub
    'метод создания верхнего слоя грунта
    Public Sub createTopLayer(ByVal corridorState As CorridorState,
                                ByVal subAsName As String,
                                ByVal drenageName As String,
                                ByVal soilName As String,
                                ByVal width As Double,
                                ByVal layerHeight As Double,
                                ByVal reminder As Double,
                                ByVal stoneTopWidth As Double,
                                ByVal flipValue As Double,
                                ByVal hasTargetOffset As Boolean,
                                ByVal pointToInsert As PointInMem)
        'объявляем коллекции элементов
        Dim Points As PointCollection
        Points = corridorState.Points
        Dim Links As LinkCollection
        Links = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        'вычисляем вспомогательные параметры
        Dim stoneSlope = 1 / 1.5
        Dim fOffset As Double = layerHeight / stoneSlope 'gravel offset
        Dim stoneUp As String = "Верх бермы"
        Dim stoneDown As String = "Низ бермы"
        '----------------------
        'Строим слой по точкам
        '----------------------
        Dim Point1 As Point = Points.Add(pointToInsert.Offset + fOffset * flipValue, pointToInsert.Elevation + reminder, stoneUp)
        'ограничимгоризонтальное смещение точки2 положением опорной точки (лицевой гранью нижележащей панели)
        Dim p2Offset = reminder / stoneSlope
        If p2Offset > Math.Abs(pointToInsert.Offset - Point1.Offset) Then
            p2Offset = Math.Abs(pointToInsert.Offset - Point1.Offset)
        End If
        Dim Point2 As Point = Points.Add(Point1.Offset - p2Offset * flipValue, pointToInsert.Elevation, stoneDown)
        Dim Point3 As Point = Points.Add(Point2.Offset + (stoneTopWidth + 2 * (reminder / stoneSlope)) * flipValue, Point2.Elevation, stoneDown)
        Dim Point4 As Point = Points.Add(Point1.Offset + stoneTopWidth * flipValue, Point1.Elevation, stoneUp)
        Dim Point5 As Point
        Dim Point6 As Point
        If hasTargetOffset Then
            Point5 = Points.Add(width * flipValue, Point1.Elevation, "")
            Point6 = Points.Add(width * flipValue, Point2.Elevation, "")
        Else
            Point5 = Points.Add(pointToInsert.Offset + width * flipValue, Point1.Elevation, "")
            Point6 = Points.Add(pointToInsert.Offset + width * flipValue, Point2.Elevation, "")
        End If

        Dim Link1 As Link = Links.Add(Point1, Point2, drenageName)
        Dim Link2 As Link = Links.Add(Point2, Point3, drenageName)
        Dim Link3 As Link = Links.Add(Point3, Point4, drenageName)
        Dim Link4 As Link = Links.Add(Point4, Point1, drenageName)
        Dim Link5 As Link = Links.Add(Point4, Point3, "")
        Dim Link6 As Link = Links.Add(Point3, Point6, "")
        Dim Link7 As Link = Links.Add(Point6, Point5, soilName)
        Dim Link8 As Link = Links.Add(Point5, Point4, soilName)

        Dim gLinks As Link() = {Link1, Link2, Link3, Link4}
        Dim drenShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(gLinks, drenageName)
        Dim sLinks As Link() = {Link5, Link6, Link7, Link8}
        Dim soilShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(sLinks, soilName)

    End Sub
    'метод создания Геопанели
    Private Sub createPanel(ByVal corridorState As CorridorState,
                                ByVal layerHeight As Double,
                                ByVal faceAngle As Double,
                                ByVal flipValue As Double,
                                ByVal pointToInsert As PointInMem)
        'объявляем коллекции элементов
        Dim Points As PointCollection
        Points = corridorState.Points
        Dim Links As LinkCollection
        Links = corridorState.Links
        Dim dOffset = layerHeight * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        Dim Point1 As Point = Points.Add(pointToInsert.Offset + dOffset, pointToInsert.Elevation + layerHeight, "")
        Dim Point2 As Point = Points.Add(pointToInsert.Offset, pointToInsert.Elevation, "")
        Dim Point3 As Point = Points.Add(pointToInsert.Offset + layerHeight * flipValue, pointToInsert.Elevation, "")

        Dim Link1 As Link = Links.Add(Point1, Point2, "Облицовочная геопанель")
        Dim Link2 As Link = Links.Add(Point2, Point3, "")
        Dim Link3 As Link = Links.Add(Point1, Point3, "")

    End Sub
    'метод создания щебеночной подушки
    Private Sub createSubbase(ByVal corridorState As CorridorState,
                              ByVal flipValue As Double,
                              ByVal width As Double,
                              ByVal height As Double,
                              ByVal baseName As String,
                              ByVal gridName As String,
                              ByVal pointToInsert As PointInMem)

        Dim Points As PointCollection = corridorState.Points
        Dim Links As LinkCollection = corridorState.Links
        Dim Shapes As ShapeCollection = corridorState.Shapes
        Dim pitName As String() = {baseName, "Котлован"}
        'Dim gridN As String() = {baseName, gridName}
        Dim fOffset = 0.3 'отступ от лицевой грани нижней панели (оси стенки)

        Dim Point1 As Point = Points.Add(pointToInsert.Offset - fOffset * flipValue, pointToInsert.Elevation, baseName)
        Dim Point2 As Point = Points.Add(Point1.Offset - height * flipValue, Point1.Elevation - height, pitName)
        Dim Point3 As Point = Points.Add(Point2.Offset + (2 * height + fOffset + width) * flipValue, Point2.Elevation, pitName)
        Dim Point4 As Point = Points.Add(pointToInsert.Offset + width * flipValue, Point1.Elevation, baseName)

        Dim Link1 As Link = Links.Add(Point1, Point2, baseName)
        Dim Link2 As Link = Links.Add(Point2, Point3, baseName)
        Dim Link3 As Link = Links.Add(Point3, Point4, baseName)
        Dim Link4 As Link = Links.Add(Point4, Point1, baseName)

        Dim Shape1 As Shape = Shapes.Add(Link1, Link2, Link3, Link4, baseName)

        Dim Link5 As Link = Links.Add(Point2, Point3, gridName)

    End Sub
    'метод для добавления шага блока
    Public Sub createAddStations(tm As DBTransactionManager, corridorState As CorridorState, blockStep As Double, blockLength As Double, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        Dim sliseStep = blockLength / 2
        Do While stationCurr < endSt
            Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
            Dim remainder = wallHeight Mod blockStep
            If Math.Abs(remainder) < 0.001 Then 'уточнение расстояния до вертикального скачка кратное длине половины облицовочного блока
                Dim rem1 = stationCurr Mod sliseStep
                Dim backSlice = stationCurr - rem1
                Dim rem2 = sliseStep - rem1
                Dim frontSlice = stationCurr + rem2
                Dim backH = target.GetElevation(alignmentId, backSlice)
                Dim frontH = target.GetElevation(alignmentId, frontSlice)
                If frontH <= backH Then
                    sectionsToAdd.Add(backSlice)
                    sectionsToAddStep.Add(backSlice + 0.001)
                    stationCurr = backSlice + sliseStep + 0.001
                Else
                    sectionsToAdd.Add(frontSlice)
                    sectionsToAddStep.Add(frontSlice + 0.001)
                    stationCurr = frontSlice + 0.001
                End If
            End If
            stationCurr += stateStep
        Loop

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description1 = "доп.сечения облицовочных блоков " + baseline.Name
                            If info.Description = description1 Then
                                reg.DeleteStation(info.Station)
                            End If
                            Dim description2 = "скачок облицовки " + baseline.Name
                            If info.Description = description2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff1 = sectionsToAdd.Except(assemblyStations)
                        Dim diff2 = sectionsToAddStep.Except(assemblyStations)
                        For Each station In diff1
                            Try
                                reg.AddStation(station, "доп.сечения облицовочных блоков " + baseline.Name)
                            Catch

                            End Try
                        Next
                        For Each station In diff2
                            Try
                                reg.AddStation(station, "скачок облицовки " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
    'условие для пересчета высоты облицовки (создание ступени)
    Public Function isStep(tm As DBTransactionManager, corridorState As CorridorState, stationCurr As Double)
        Dim result As Boolean = False
        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'получаем свойства доп сечений
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "скачок облицовки " + baseline.Name
                            If info.Description = description And stationCurr = info.Station Then
                                result = True
                            End If
                        Next
                    End If
                Next
            End If
        Next
        Return result
    End Function
End Class

