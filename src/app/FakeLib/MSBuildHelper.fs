﻿[<AutoOpen>]
module Fake.MSBuildHelper

open System
open System.Text
open System.IO
open System.Configuration
open System.Xml
open System.Xml.Linq

type MSBuildProject = XDocument

/// MSBuild exe fileName
let msBuildExe =   
    let ev = environVar "MSBuild"
    if not (isNullOrEmpty ev) then ev else
        if "true".Equals(ConfigurationManager.AppSettings.["IgnoreMSBuild"],StringComparison.OrdinalIgnoreCase) then 
            String.Empty 
        else 
            findPath "MSBuildPath" "MSBuild.exe"


let msbuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003"
let xname name = XName.Get(name,msbuildNamespace)

let loadProject (projectFileName:string) : MSBuildProject = 
    MSBuildProject.Load(projectFileName,LoadOptions.PreserveWhitespace)

let internal getReferenceElements elementName projectFileName (doc:XDocument) =
    let fi = fileInfo projectFileName
    doc
      .Descendants(xname "Project")
      .Descendants(xname "ItemGroup")
      .Descendants(xname elementName)
        |> Seq.map(fun e -> 
            let a = e.Attribute(XName.Get "Include")
            let value = a.Value
            let fileName =
                if value.StartsWith(".." + directorySeparator) || (not <| value.Contains directorySeparator) then
                    fi.Directory.FullName @@ value
                else
                    value
            a,fileName |> FullName)   


let processReferences elementName f projectFileName (doc:XDocument) =
    let fi = fileInfo projectFileName
    doc
        |> getReferenceElements elementName projectFileName
        |> Seq.iter (fun (a,fileName) -> a.Value <- f fileName)
    doc

let rec getProjectReferences projectFileName= 
    let doc = loadProject projectFileName
    let references =
        getReferenceElements "ProjectReference" projectFileName doc
            |> Seq.map snd

    references
      |> Seq.map getProjectReferences
      |> Seq.concat
      |> Seq.append references
      |> Set.ofSeq

/// Runs a msbuild project
let build outputPath targets properties project =
    traceStartTask "MSBuild" project
    let targetsA = sprintf "/target:%s" targets |> toParam
    let output = 
        if isNullOrEmpty outputPath then "" else
        outputPath
          |> FullName
          |> trimSeparator
          |> sprintf "/p:OutputPath=\"%s\"\\"
        
    let props = 
        properties
          |> Seq.map (fun (key,value) -> sprintf " /p:%s=%s " key value)
          |> separated ""
 
    let args = toParam project + targetsA + props + output
    logfn "Building project: %s\n  %s %s" project msBuildExe args
    if not (execProcess3 (fun info ->  
        info.FileName <- msBuildExe
        info.Arguments <- args) TimeSpan.MaxValue)
    then failwithf "Building %s project failed." project

    traceEndTask "MSBuild" project

/// Builds the given project files and collects the output files
let MSBuild outputPath targets properties projects = 
    let projects = projects |> Seq.toList
    let dependencies =
        projects 
            |> List.map getProjectReferences
            |> Set.unionMany

    projects
      |> List.filter (fun project -> not <| Set.contains project dependencies)
      |> List.iter (build outputPath targets properties)

    !+ (outputPath + "/**/*.*")
      |> Scan   

/// Builds the given project files and collects the output files
let MSBuildDebug outputPath targets = MSBuild outputPath targets ["Configuration","Debug"]

/// Builds the given project files and collects the output files
let MSBuildRelease outputPath targets = MSBuild outputPath targets ["Configuration","Release"]