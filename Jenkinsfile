pipeline {
        agent none
        stages {
            stage("Git Checkout"){
            agent any
            steps {
                    checkout([$class: 'GitSCM', branches: [[name: '*/master']], doGenerateSubmoduleConfigurations: false, extensions: [], submoduleCfg: [], userRemoteConfigs: [[url: 'https://github.com/ashokreddy7777/Test.git']]])
                }
            }
            stage("build & SonarQube analysis") {
            agent any
            steps {
              withSonarQubeEnv('sonarqube') {
                bat 'mvn clean package sonar:sonar'
              }
            }
          }
            stage("Quality Gate") {
            steps {
              timeout(time: 2, unit: 'MINUTES') {
                waitForQualityGate abortPipeline: true
              }
            }
          }
        }
      }